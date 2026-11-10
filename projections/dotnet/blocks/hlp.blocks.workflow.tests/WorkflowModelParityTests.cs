using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.Workflow.Durable;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Definitions;

using Xunit;

using Wire = Harborline.Contracts.Workflow;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// T-488 platform model parity. Oracles: the shared rows in conformance/hlp.contracts.workflow/fixtures.yaml,
/// whose expected values are data outside either validator, and for the api spellings the reader at api
/// packages/foundation-authorization/RoleGateAdmissionContracts.cs. Every fixture row runs through both models, so the
/// engine and the wire contract cannot return different verdicts on an envelope, role, standing or contract window.
/// </summary>
public sealed class WorkflowModelParityTests
{
    private const string Tenant = "tenant:acme";
    private const string Key = "invoice-approval";
    private const string Version = "1.0.0";

    private static readonly DefinitionContractWindow Window = new(1, 2, 1);

    private static string Authored(string transition = "", string action = "", string envelope = "") => $$"""
        {
          {{(envelope.Length == 0 ? "" : $"\"envelope\": {envelope},")}}
          "mutability": "Locked",
          "initialState": "Open",
          "states": [{"id": "Open", "kind": "Normal"}, {"id": "Closed", "kind": "Terminal"}],
          "triggers": [{"id": "approve", "kind": "HumanAction", "task": "approval"}],
          "transitions": [{"id": "t-close", "from": "Open", "on": "approve", "to": "Closed"{{transition}}}],
          "actions": [{"id": "a-notify", "on": {"transition": "t-close"}, "kind": "Notify", "capabilityRef": "notify.email", "classification": "AP"{{action}}}],
          "guards": []
        }
        """;

    private const string Envelope = """
        {"identity": "invoice-approval", "version": "1.0.0", "tenant": "tenant:acme", "cascadeLayer": "DomainPackage",
         "provenance": {"source": "pack"}, "retentionClass": "standard", "legalHold": true,
         "requires": [{"capability": "notify.email"}], "contract": {"major": 1, "minor": 2}}
        """;

    private static WorkflowDefinition Map(string authored)
    {
        using var document = JsonDocument.Parse(authored);
        return WorkflowDefinitionWireMapper.ToModel(document.RootElement, Tenant, Key, Version);
    }

    private static WorkflowAdmissionResult Admit(WorkflowDefinition definition, DefinitionContractWindow? window = null, RoleVocabulary? roles = null) =>
        new WorkflowAdmissionValidator(new FixtureAuthority(new() { ["notify.email"] = ActionClassification.AP }), null, roles, window).Validate(definition);

    private static IEnumerable<string> Codes(WorkflowAdmissionResult result) => result.Violations.Select(violation => violation.Code);

    [Fact]
    public void Notification_windows_match_shared_wire_cases_in_both_admission_validators()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var canonical = (Wire.WorkflowDefinition)Wire.WorkflowWire.Deserialize("WorkflowDefinition", fixture.RootElement.GetProperty("canonical").GetRawText());
        var authority = new Wire.ImmutableWorkflowAuthorityResolver(new Dictionary<string, Wire.ActionClassification> { ["notify.email"] = Wire.ActionClassification.AP });
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Where(row => row.GetProperty("id").GetString()!.StartsWith("workflows-ck-22-", StringComparison.Ordinal))
            .Where(row => row.GetProperty("id").GetString()!.Contains("digit-refused", StringComparison.Ordinal) || row.GetProperty("id").GetString() == "workflows-ck-22-trailing-newline-refused" || row.GetProperty("id").GetString() == "workflows-ck-22-notification-action-round-trip").ToArray();
        Assert.Equal(5, cases.Length);
        foreach (var row in cases)
        {
            var window = row.GetProperty("input").GetProperty("notification").GetProperty("batchingWindow").GetString()!;
            var invalid = row.TryGetProperty("expectedError", out _);
            var action = canonical.Actions[0] with { Kind = Wire.WorkflowActionKind.Notify, Notification = new Wire.WorkflowNotification { EventKey = "k", BatchingWindow = window, Tier = Wire.WorkflowNotificationTier.Observation } };
            var wire = Wire.WorkflowAdmissionValidator.Validate(canonical with { Actions = [action] }, authority);
            Assert.Equal(invalid, wire.Violations.Any(v => v.Code == "workflow.invalid_notification"));
            var engine = Admit(new WorkflowDefinition
            {
                Key = Key, Version = Version, Tenant = Tenant, InitialState = "Closed",
                States = [new WorkflowStateDef { Id = "Closed", Kind = WorkflowStateKind.Terminal }],
                Actions = [new WorkflowActionBindingDef { Id = "notify", OnState = "Closed", Kind = WorkflowActionKind.Notify, CapabilityRef = "notify.email", Classification = ActionClassification.AP, Notification = new WorkflowNotificationDef("k", window, Wire.WorkflowNotificationTier.Observation) }],
            });
            Assert.Equal(invalid, engine.Violations.Any(v => v.Code == "workflow.invalid_notification"));
        }
    }

    [Fact(DisplayName = "workflows-ck-2: the envelope round trips every member through the mapper")]
    public void Workflows_ck_2_the_envelope_round_trips_every_member_through_the_mapper()
    {
        var envelope = Map(Authored(envelope: Envelope)).Envelope!;

        Assert.Equal("invoice-approval", envelope.Identity);
        Assert.Equal("1.0.0", envelope.Version);
        Assert.Equal(Tenant, envelope.Tenant);
        Assert.Equal(WorkflowCascadeLayer.DomainPackage, envelope.CascadeLayer);
        Assert.Equal("pack", envelope.Provenance!.Value.GetProperty("source").GetString());
        Assert.Equal("standard", envelope.RetentionClass);
        Assert.True(envelope.LegalHold);
        Assert.Equal(["notify.email"], envelope.Requires.Select(requirement => requirement.Capability));
        Assert.Equal(new DefinitionContractVersion(1, 2), envelope.Contract);
    }

    [Fact(DisplayName = "workflows-ck-2: a definition without an envelope maps to null and still admits when no window is composed")]
    public void Workflows_ck_2_a_definition_without_an_envelope_maps_to_null_and_still_admits_when_no_window_is_composed()
    {
        var definition = Map(Authored());

        Assert.Null(definition.Envelope);
        Assert.Empty(Codes(Admit(definition)));
        Assert.Equal([WorkflowAdmissionCodes.EnvelopeRequired], Codes(Admit(definition, Window)));
    }

    [Theory(DisplayName = "workflows-ck-2: the contract window decides admission")]
    [InlineData("""{"major": 1, "minor": 2}""", null)]
    [InlineData("""{"major": 1, "minor": 3}""", "definition.contract.out_of_window")]
    [InlineData("""{"major": 2, "minor": 0}""", "definition.contract.out_of_window")]
    [InlineData("""{"major": 0, "minor": 9}""", "definition.contract.out_of_window")]
    [InlineData("""{"major": -1, "minor": 0}""", "definition.contract.missing")]
    public void Workflows_ck_2_the_contract_window_decides_admission(string contract, string? expected)
    {
        var node = JsonNode.Parse(Envelope)!;
        node["contract"] = JsonNode.Parse(contract);

        var codes = Codes(Admit(Map(Authored(envelope: node.ToJsonString())), Window));

        Assert.Equal(expected is null ? [] : [expected], codes);
    }

    [Fact(DisplayName = "workflows-ck-2: a missing contract reads as null and is refused under a window")]
    public void Workflows_ck_2_a_missing_contract_reads_as_null_and_is_refused_under_a_window()
    {
        var node = JsonNode.Parse(Envelope)!.AsObject();
        node.Remove("contract");
        var definition = Map(Authored(envelope: node.ToJsonString()));

        Assert.Null(definition.Envelope!.Contract);
        Assert.Equal(["definition.contract.missing"], Codes(Admit(definition, Window)));
        Assert.Contains(Admit(definition, Window).Violations, violation => violation.Locator == "/envelope/contract");
    }

    [Theory(DisplayName = "workflows-ck-2: an envelope naming another revision is refused")]
    [InlineData("identity", "other")]
    [InlineData("version", "9.9.9")]
    [InlineData("tenant", "tenant:other")]
    public void Workflows_ck_2_an_envelope_naming_another_revision_is_refused(string member, string value)
    {
        var node = JsonNode.Parse(Envelope)!;
        node[member] = value;

        Assert.Equal([WorkflowAdmissionCodes.EnvelopeMismatch], Codes(Admit(Map(Authored(envelope: node.ToJsonString())))));
    }

    [Theory(DisplayName = "workflows-ck-2: a malformed envelope member fails closed at the mapper")]
    [InlineData("cascadeLayer", "\"Instance\"")]
    [InlineData("cascadeLayer", "\"tenantconfiguration\"")]
    [InlineData("cascadeLayer", "\"4\"")]
    [InlineData("legalHold", "\"true\"")]
    [InlineData("requires", "{}")]
    [InlineData("contract", "{\"major\": 1}")]
    [InlineData("contract", "{\"major\": \"1\", \"minor\": 2}")]
    [InlineData("contract", "null")]
    public void Workflows_ck_2_a_malformed_envelope_member_fails_closed_at_the_mapper(string member, string value)
    {
        var node = JsonNode.Parse(Envelope)!;
        node[member] = JsonNode.Parse(value);

        Assert.Throws<ArgumentException>(() => Map(Authored(envelope: node.ToJsonString())));
    }

    [Theory(DisplayName = "workflows-ck-2: an absent or empty governance member never admits")]
    [InlineData("provenance")]
    [InlineData("retentionClass")]
    public void Workflows_ck_2_an_absent_or_empty_governance_member_never_admits(string member)
    {
        var node = JsonNode.Parse(Envelope)!.AsObject();
        node.Remove(member);

        var refused = false;
        try { refused = !Admit(Map(Authored(envelope: node.ToJsonString()))).IsValid; }
        catch (ArgumentException) { refused = true; }

        Assert.True(refused);
    }

    [Fact(DisplayName = "workflows-ck-2: the mapped provenance outlives the authored document")]
    public void Workflows_ck_2_the_mapped_provenance_outlives_the_authored_document()
    {
        WorkflowDefinition definition;
        using (var document = JsonDocument.Parse(Authored(envelope: Envelope)))
            definition = WorkflowDefinitionWireMapper.ToModel(document.RootElement, Tenant, Key, Version);

        Assert.Equal("pack", definition.Envelope!.Provenance!.Value.GetProperty("source").GetString());
    }

    [Theory(DisplayName = "workflows-ck-15: the four sealed positions admit")]
    [InlineData("Anyone")]
    [InlineData("Author")]
    [InlineData("Reviewer")]
    [InlineData("Approver")]
    public void Workflows_ck_15_the_four_sealed_positions_admit(string name)
    {
        var definition = Map(Authored(transition: $$""", "requiredRoles": ["sys.roles/{{name}}"]"""));

        Assert.Equal([new RoleReference("sys.roles", name)], definition.Transitions[0].RequiredRoles);
        Assert.Empty(Codes(Admit(definition)));
    }

    [Theory(DisplayName = "workflows-ck-15: nothing else is in sys roles even with no vocabulary composed")]
    [InlineData("Owner")]
    [InlineData("approver")]
    [InlineData("Administrator")]
    public void Workflows_ck_15_nothing_else_is_in_sys_roles_even_with_no_vocabulary_composed(string name)
    {
        var definition = Map(Authored(action: $$""", "requiredRoles": [{"vocabulary": "sys.roles", "name": "{{name}}"}]"""));

        Assert.Equal([WorkflowAdmissionCodes.UnknownRoleReference], Codes(Admit(definition)));
    }

    [Fact(DisplayName = "workflows-auth-10: a role is read from the api spelling and the wire object alike")]
    public void Workflows_auth_10_a_role_is_read_from_the_api_spelling_and_the_wire_object_alike()
    {
        var definition = Map(Authored(
            transition: """, "requiredRoles": ["tax.roles/preparer", {"vocabulary": "sys.platform-roles", "name": "auditor"}]""",
            action: """, "requiredRoles": ["sys.roles/Approver"]"""));

        Assert.Equal([RoleReference.Domain("preparer"), RoleReference.Auditor], definition.Transitions[0].RequiredRoles);
        Assert.Equal([new RoleReference("sys.roles", "Approver")], definition.Actions[0].RequiredRoles);
    }

    [Theory(DisplayName = "workflows-auth-10: the api compatibility spellings resolve into the platform vocabulary")]
    [InlineData("Admin", "administrator")]
    [InlineData("Member", "administrator")]
    [InlineData("node:operator", "administrator")]
    [InlineData("Viewer", "auditor")]
    public void Workflows_auth_10_the_api_compatibility_spellings_resolve_into_the_platform_vocabulary(string spelling, string name)
    {
        var definition = Map(Authored(transition: $$""", "requiredRoles": ["{{spelling}}"]"""));

        Assert.Equal([new RoleReference(RoleVocabularies.Platform, name)], definition.Transitions[0].RequiredRoles);
    }

    [Theory(DisplayName = "workflows-auth-10: an unreadable role is refused with the api code and its index")]
    [InlineData("\"handler\"", "transition.requiredRoles[0]")]
    [InlineData("\"a/b/c\"", "transition.requiredRoles[0]")]
    [InlineData("\"/b\"", "transition.requiredRoles[0]")]
    [InlineData("\"a/\"", "transition.requiredRoles[0]")]
    [InlineData("7", "transition.requiredRoles[0]")]
    [InlineData("{\"vocabulary\": \"tax.roles\"}", "transition.requiredRoles[0]")]
    public void Workflows_auth_10_an_unreadable_role_is_refused_with_the_api_code_and_its_index(string entry, string field)
    {
        var error = Assert.Throws<WorkflowGateReferenceException>(() => Map(Authored(transition: $$""", "requiredRoles": [{{entry}}]""")));

        Assert.Equal("authorization.gate_reference.required_roles_invalid", error.Code);
        Assert.Equal(field, error.Field);
    }

    [Fact(DisplayName = "workflows-auth-10: a role outside the three vocabularies is refused and access resolves the rest")]
    public void Workflows_auth_10_a_role_outside_the_three_vocabularies_is_refused_and_access_resolves_the_rest()
    {
        Assert.Equal([WorkflowAdmissionCodes.UnknownRoleReference],
            Codes(Admit(Map(Authored(transition: """, "requiredRoles": ["sys.other/approver"]""")))));

        var vocabulary = RoleVocabulary.FromApi([]);
        var domain = Map(Authored(transition: """, "requiredRoles": ["tax.roles/preparer"]"""));
        Assert.Empty(Codes(Admit(domain)));
        Assert.Equal([WorkflowAdmissionCodes.UnknownRoleReference], Codes(Admit(domain, roles: vocabulary)));
    }

    [Fact(DisplayName = "workflows-auth-11: standings accumulate in order from both spellings")]
    public void Workflows_auth_11_standings_accumulate_in_order_from_both_spellings()
    {
        var definition = Map(Authored(
            transition: """, "requiredStandings": ["assigned-approver", {"name": "not.the_author"}]""",
            action: """, "requiredStandings": ["assigned-approver"]"""));

        Assert.Equal([new RecordStandingReference("assigned-approver"), new RecordStandingReference("not.the_author")], definition.Transitions[0].RequiredStandings);
        Assert.Equal([new RecordStandingReference("assigned-approver")], definition.Actions[0].RequiredStandings);
        Assert.Empty(Codes(Admit(definition)));
    }

    [Theory(DisplayName = "workflows-auth-11: a standing that is not a bare name cannot be stated")]
    [InlineData("sys.platform-roles/administrator")]
    [InlineData("*")]
    [InlineData("!author")]
    [InlineData("")]
    [InlineData("1st")]
    [InlineData("author\\n")]
    public void Workflows_auth_11_a_standing_that_is_not_a_bare_name_cannot_be_stated(string name)
    {
        var error = Assert.Throws<WorkflowGateReferenceException>(() => Map(Authored(action: $$""", "requiredStandings": ["{{name}}"]""")));

        Assert.Equal("authorization.gate_reference.required_standings_invalid", error.Code);
        Assert.Equal("action.requiredStandings[0]", error.Field);
    }

    [Fact(DisplayName = "workflows-auth-11: a directly built standing is still refused at admission")]
    public void Workflows_auth_11_a_directly_built_standing_is_still_refused_at_admission()
    {
        var definition = Map(Authored());
        var built = new WorkflowDefinition
        {
            Key = definition.Key,
            Version = definition.Version,
            Tenant = definition.Tenant,
            InitialState = definition.InitialState,
            States = definition.States,
            Triggers = definition.Triggers,
            Actions = definition.Actions,
            Transitions =
            [
                new WorkflowTransitionDef
                {
                    Id = "t-close", From = "Open", On = "approve", To = "Closed",
                    RequiredStandings = [new RecordStandingReference("sys.platform-roles/administrator")],
                },
            ],
        };

        Assert.Equal([WorkflowAdmissionCodes.InvalidStanding], Codes(Admit(built)));
    }

    [Fact(DisplayName = "workflows-ck-2, workflows-ck-15, workflows-auth-10, workflows-auth-11: both validators return the fixture verdict on every shared row")]
    public void Workflows_ck_2_ck_15_auth_10_auth_11_both_validators_return_the_fixture_verdict_on_every_shared_row()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var root = fixture.RootElement;
        var authority = root.GetProperty("authority").EnumerateObject().ToDictionary(row => row.Name, row => row.Value.GetString()!, StringComparer.Ordinal);
        var engineAuthority = new FixtureAuthority(authority.ToDictionary(row => row.Key, row => Enum.Parse<ActionClassification>(row.Value)));
        var wireAuthority = new Wire.ImmutableWorkflowAuthorityResolver(authority.ToDictionary(row => row.Key, row => Enum.Parse<Wire.ActionClassification>(row.Value)));
        var roles = RoleVocabulary.FromApi(JsonSerializer.Deserialize<RoleDefinition[]>(root.GetProperty("roleDefinitions").GetRawText())!);
        var window = root.GetProperty("contractWindow");
        var engineWindow = new DefinitionContractWindow(window.GetProperty("major").GetInt32(), window.GetProperty("minor").GetInt32(), window.GetProperty("oldestMajor").GetInt32());
        var wireWindow = new Wire.WorkflowContractWindow(engineWindow.Major, engineWindow.Minor, engineWindow.OldestMajor);

        var compared = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var operation = row.GetProperty("operation").GetString()!;
            if (!operation.StartsWith("admission.", StringComparison.Ordinal)) continue;
            var id = row.GetProperty("id").GetString()!;
            var windowed = row.TryGetProperty("window", out _);
            var authored = JsonNode.Parse(root.GetProperty("canonical").GetRawText())!.AsObject();
            if (row.TryGetProperty("mutation", out var mutation)) Mutate(authored, mutation, root);
            var text = authored.ToJsonString();
            var expected = row.GetProperty("expected").GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).Order(StringComparer.Ordinal).ToArray();

            var wire = Wire.WorkflowAdmissionValidator.Validate(
                (Wire.WorkflowDefinition)Wire.WorkflowWire.Deserialize("WorkflowDefinition", text), wireAuthority, roles, windowed ? wireWindow : null);
            Assert.True(expected.SequenceEqual(wire.Violations.Select(violation => violation.Code).Distinct().Order(StringComparer.Ordinal)), $"{id} wire: {string.Join(",", wire.Violations.Select(violation => violation.Code))}");

            using var document = JsonDocument.Parse(text);
            if (row.TryGetProperty("engineParseRefuses", out var parseCode))
            {
                // The engine's reader carries the api's fail-closed refusal, so this shape never reaches its validator.
                var error = Assert.Throws<WorkflowGateReferenceException>(() => WorkflowDefinitionWireMapper.ToModel(document.RootElement, Tenant, Key, Version));
                Assert.Equal(parseCode.GetString(), error.Code);
            }
            else
            {
                var model = WorkflowDefinitionWireMapper.ToModel(document.RootElement, Tenant, Key, Version);
                var engine = new WorkflowAdmissionValidator(engineAuthority, null, roles, windowed ? engineWindow : null).Validate(model);
                Assert.True(expected.SequenceEqual(engine.Violations.Select(violation => violation.Code).Distinct().Order(StringComparer.Ordinal)), $"{id} engine: {string.Join(",", engine.Violations.Select(violation => violation.Code))}");
            }
            compared++;
        }

        Assert.True(compared >= 30, $"expected the shared admission rows to run, ran {compared}");
    }

    [Fact(DisplayName = "workflows-ck-2: the shared envelope round trips the same through both models")]
    public void Workflows_ck_2_the_shared_envelope_round_trips_the_same_through_both_models()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var authored = JsonNode.Parse(fixture.RootElement.GetProperty("canonical").GetRawText())!.AsObject();
        authored["envelope"] = JsonNode.Parse(fixture.RootElement.GetProperty("envelope").GetRawText());
        var text = authored.ToJsonString();
        var wire = ((Wire.WorkflowDefinition)Wire.WorkflowWire.Deserialize("WorkflowDefinition", text)).Envelope.Value!;
        using var document = JsonDocument.Parse(text);
        var engine = WorkflowDefinitionWireMapper.ToModel(document.RootElement, Tenant, Key, Version).Envelope!;

        Assert.Equal(wire.Identity, engine.Identity);
        Assert.Equal(wire.Version, engine.Version);
        Assert.Equal(wire.Tenant, engine.Tenant);
        Assert.Equal(wire.CascadeLayer.ToString(), engine.CascadeLayer.ToString());
        Assert.Equal(wire.Provenance.GetRawText(), engine.Provenance!.Value.GetRawText());
        Assert.Equal(wire.RetentionClass, engine.RetentionClass);
        Assert.Equal(wire.LegalHold, engine.LegalHold);
        Assert.Equal(wire.Requires.Select(row => row.Capability), engine.Requires.Select(row => row.Capability));
        Assert.Equal(wire.Contract.Value!.Major, engine.Contract!.Major);
        Assert.Equal(wire.Contract.Value!.Minor, engine.Contract!.Minor);
    }

    // The same fixture vocabulary the contract's conformance test applies.
    private static void Mutate(JsonObject definition, JsonElement mutation, JsonElement root)
    {
        var action = definition["actions"]!.AsArray()[0]!.AsObject();
        var transition = definition["transitions"]!.AsArray()[0]!.AsObject();
        // Role and standing mutations target the first HumanAction transition: since T-1176 Q1 a gate on an
        // autonomous transition is its own refusal (gate_on_autonomous_step), tested by the autonomous* keys.
        var humanTriggers = definition["triggers"]!.AsArray().Where(t => (string?)t!["kind"] == "HumanAction").Select(t => (string?)t!["id"]).ToHashSet();
        var gated = definition["transitions"]!.AsArray().First(t => humanTriggers.Contains((string?)t!["on"]))!.AsObject();
        JsonNode Missing() => new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "Missing" };
        if (mutation.TryGetProperty("actionTransition", out var actionTransition)) action["on"] = new JsonObject { ["transition"] = actionTransition.GetString() };
        if (mutation.TryGetProperty("actionClassification", out var classification)) action["classification"] = classification.GetString();
        if (mutation.TryGetProperty("transitionGuard", out var guard)) transition["guard"] = guard.GetString();
        if (mutation.TryGetProperty("terminalOutgoing", out _))
            definition["transitions"]!.AsArray().Add(new JsonObject { ["id"] = "t-terminal", ["from"] = "Posted", ["on"] = "approve", ["to"] = "Rejected" });
        if (mutation.TryGetProperty("unknownActionRole", out _)) action["requiredRoles"] = new JsonArray(Missing());
        if (mutation.TryGetProperty("unknownTransitionRole", out _)) gated["requiredRoles"] = new JsonArray(Missing());
        if (mutation.TryGetProperty("envelope", out _)) definition["envelope"] = JsonNode.Parse(root.GetProperty("envelope").GetRawText());
        var envelope = definition["envelope"]?.AsObject();
        if (mutation.TryGetProperty("envelopeContract", out var contract))
        {
            if (contract.ValueKind == JsonValueKind.Null) envelope!.Remove("contract");
            else envelope!["contract"] = JsonNode.Parse(contract.GetRawText());
        }
        if (mutation.TryGetProperty("envelopeIdentity", out var identity)) envelope!["identity"] = identity.GetString();
        if (mutation.TryGetProperty("envelopeRetentionClass", out var retention)) envelope!["retentionClass"] = retention.GetString();
        if (mutation.TryGetProperty("envelopeRequirement", out var requirement)) envelope!["requires"] = new JsonArray(new JsonObject { ["capability"] = requirement.GetString() });
        if (mutation.TryGetProperty("transitionRole", out var transitionRole)) gated["requiredRoles"] = new JsonArray(JsonNode.Parse(transitionRole.GetRawText()));
        if (mutation.TryGetProperty("actionRole", out var actionRole)) action["requiredRoles"] = new JsonArray(JsonNode.Parse(actionRole.GetRawText()));
        if (mutation.TryGetProperty("transitionStanding", out var transitionStanding)) gated["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = transitionStanding.GetString() });
        // T-1176 mixed reachability: an action firing on entry to a state, and an autonomous edge into that state.
        if (mutation.TryGetProperty("actionState", out var actionState)) action["on"] = new JsonObject { ["state"] = actionState.GetString() };
        if (mutation.TryGetProperty("autonomousEdgeInto", out var edgeInto))
        {
            var autonomousTrigger = definition["triggers"]!.AsArray().First(t => (string?)t!["kind"] != "HumanAction")!["id"]!.GetValue<string>();
            var from = (string?)gated["from"];
            definition["transitions"]!.AsArray().Add(new JsonObject { ["id"] = "t-autonomous-into", ["from"] = from, ["on"] = autonomousTrigger, ["to"] = edgeInto.GetString() });
        }
        if (mutation.TryGetProperty("autonomousTransitionRole", out var autonomousRole)) transition["requiredRoles"] = new JsonArray(JsonNode.Parse(autonomousRole.GetRawText()));
        if (mutation.TryGetProperty("autonomousTransitionStanding", out var autonomousStanding)) transition["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = autonomousStanding.GetString() });
        if (mutation.TryGetProperty("actionStanding", out var actionStanding)) action["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = actionStanding.GetString() });
    }

    private static string FindFixture()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.workflow", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Unable to locate conformance/hlp.contracts.workflow/fixtures.yaml.");
    }

    private sealed class FixtureAuthority(Dictionary<string, ActionClassification> rows) : ICapabilityAuthorityRegistry
    {
        public ActionClassification AuthorityOf(string capabilityRef) => rows.GetValueOrDefault(capabilityRef, ActionClassification.CP);
        public bool IsRegistered(string capabilityRef) => rows.ContainsKey(capabilityRef);
    }
}
