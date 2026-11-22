using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Scheduling.Tests;

// Oracles: T-491/definition-wire-literal-proof acceptance 1–3, DES-0022 ck-13/ck-19,
// ADR-0006 and frozen Scheduling interface revision 14. Encoder and decoder fixtures are independent.
public sealed class ScheduleDefinitionWireLiteralProofTests
{
    private const string Literal = """
        {
          "envelope": {
            "identity":"inspection.quarterly", "version":"1.0.0", "tenant":"tenant-a",
            "cascade_layer":"domain_package", "provenance":{"pack":"general"},
            "retention_class":"standard", "legal_hold":true,
            "requires":[{"capability":"platform.scheduling"}], "contract":{"major":1,"minor":0}
          },
          "name":"Quarterly", "starts":{"kind":"form","target_id":"forms.inspection"},
          "on_type_id":"types.boiler", "due_date":"2027-01-01T00:00:00Z",
          "window":{"earliest":"2027-01-01T00:00:00Z","latest":"2027-01-31T00:00:00+02:00"},
          "legality_ref":"rules.eu-561@2026"
        }
        """;

    private static ScheduleDefinition TypedDefinition()
    {
        using var provenance = JsonDocument.Parse("""{"pack":"general"}""");
        return new(
            new("inspection.quarterly", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
                provenance.RootElement.Clone(), "standard", true,
                [new("platform.scheduling")], new DefinitionContractVersion(1, 0)),
            "Quarterly", new(ScheduleStartKind.Form, "forms.inspection"),
            OnTypeId: "types.boiler", DueDate: "2027-01-01T00:00:00Z",
            Window: new("2027-01-01T00:00:00Z", "2027-01-31T00:00:00+02:00"),
            LegalityRef: "rules.eu-561@2026");
    }

    [Fact(DisplayName = "scheduling-ck-13 / scheduling-ck-19: typed DTO encodes exact wire members and values")]
    public void EncodesLiteralMembersAndValues()
    {
        using var json = JsonDocument.Parse(ScheduleDefinitionJson.Serialize(TypedDefinition()));
        var root = json.RootElement;
        Properties(root, "envelope", "name", "starts", "on_type_id", "due_date", "window", "legality_ref", "problem_class");
        Assert.Equal("Quarterly", root.GetProperty("name").GetString());
        Assert.Equal("types.boiler", root.GetProperty("on_type_id").GetString());
        Assert.Equal("2027-01-01T00:00:00Z", root.GetProperty("due_date").GetString());
        Assert.Equal("rules.eu-561@2026", root.GetProperty("legality_ref").GetString());
        Assert.Equal("None", root.GetProperty("problem_class").GetString());
        var starts = root.GetProperty("starts");
        Properties(starts, "kind", "target_id");
        Assert.Equal("form", starts.GetProperty("kind").GetString());
        Assert.Equal("forms.inspection", starts.GetProperty("target_id").GetString());
        var envelope = root.GetProperty("envelope");
        Properties(envelope, "identity", "version", "tenant", "cascade_layer", "provenance", "retention_class", "legal_hold", "requires", "contract");
        Assert.Equal("inspection.quarterly", envelope.GetProperty("identity").GetString());
        Assert.Equal("1.0.0", envelope.GetProperty("version").GetString());
        Assert.Equal("tenant-a", envelope.GetProperty("tenant").GetString());
        Assert.Equal("domain_package", envelope.GetProperty("cascade_layer").GetString());
        Properties(envelope.GetProperty("provenance"), "pack");
        Assert.Equal("general", envelope.GetProperty("provenance").GetProperty("pack").GetString());
        Assert.Equal("standard", envelope.GetProperty("retention_class").GetString());
        Assert.True(envelope.GetProperty("legal_hold").GetBoolean());
        var requirement = Assert.Single(envelope.GetProperty("requires").EnumerateArray());
        Properties(requirement, "capability");
        Assert.Equal("platform.scheduling", requirement.GetProperty("capability").GetString());
        var contract = envelope.GetProperty("contract");
        Properties(contract, "major", "minor");
        Assert.Equal(1, contract.GetProperty("major").GetInt32());
        Assert.Equal(0, contract.GetProperty("minor").GetInt32());
        var window = root.GetProperty("window");
        Properties(window, "earliest", "latest");
        Assert.Equal("2027-01-01T00:00:00Z", window.GetProperty("earliest").GetString());
        Assert.Equal("2027-01-31T00:00:00+02:00", window.GetProperty("latest").GetString());
    }

    [Theory(DisplayName = "scheduling-ck-13 / scheduling-ck-19: typed starts variants encode named kinds")]
    [InlineData(ScheduleStartKind.Workflow, "workflows.inspect", "workflow")]
    [InlineData(ScheduleStartKind.WorkItem, "work-items.inspect", "work_item")]
    public void EncodesLiteralStartsVariants(ScheduleStartKind kind, string target, string wireKind)
    {
        using var json = JsonDocument.Parse(ScheduleDefinitionJson.Serialize(TypedDefinition() with { Starts = new(kind, target) }));
        var starts = json.RootElement.GetProperty("starts");
        Properties(starts, "kind", "target_id");
        Assert.Equal(wireKind, starts.GetProperty("kind").GetString());
        Assert.Equal(target, starts.GetProperty("target_id").GetString());
    }

    [Fact(DisplayName = "scheduling-ck-13 / scheduling-ck-19: independent JSON literal decodes every DTO member")]
    public void DecodesIndependentLiteral() => AssertDecoded(ScheduleDefinitionJson.Deserialize(Literal));

    [Theory(DisplayName = "scheduling-ck-13 / scheduling-ck-19: independent starts literals decode all named kinds")]
    [InlineData("""{"kind":"form","target_id":"forms.inspection"}""", ScheduleStartKind.Form, "forms.inspection")]
    [InlineData("""{"kind":"workflow","target_id":"workflows.inspect"}""", ScheduleStartKind.Workflow, "workflows.inspect")]
    [InlineData("""{"kind":"work_item","target_id":"work-items.inspect"}""", ScheduleStartKind.WorkItem, "work-items.inspect")]
    public void DecodesLiteralStartsVariants(string starts, ScheduleStartKind kind, string target)
    {
        var json = Literal.Replace("""{"kind":"form","target_id":"forms.inspection"}""", starts, StringComparison.Ordinal);
        var decoded = ScheduleDefinitionJson.Deserialize(json);
        Assert.Equal(kind, decoded.Starts.Kind);
        Assert.Equal(target, decoded.Starts.TargetId);
    }

    [Theory(DisplayName = "scheduling-ck-13 / scheduling-ck-19: each unreadable member throws and its sole repair fully decodes")]
    [InlineData("\"name\":", "\"Name\":")]
    [InlineData("\"name\":", "\"unexpected_member\":true, \"name\":")]
    [InlineData("\"kind\":\"form\"", "\"kind\":0")]
    [InlineData("\"legal_hold\":true,", "")]
    public void RefusesSingleInvalidChangeAndAcceptsRepair(string before, string after)
    {
        var invalid = Literal.Replace(before, after, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => ScheduleDefinitionJson.Deserialize(invalid));
        // Start from the same hand-authored fixture and undo only this case's member edit.
        var repaired = after.Length == 0
            ? invalid.Replace("\"requires\":", "\"legal_hold\":true, \"requires\":", StringComparison.Ordinal)
            : invalid.Replace(after, before, StringComparison.Ordinal);
        AssertDecoded(ScheduleDefinitionJson.Deserialize(repaired));
    }

    private static void AssertDecoded(ScheduleDefinition definition)
    {
        var envelope = Assert.IsType<ScheduleDefinitionEnvelope>(definition.Envelope);
        Assert.Equal("inspection.quarterly", envelope.Identity);
        Assert.Equal("1.0.0", envelope.Version);
        Assert.Equal("tenant-a", envelope.Tenant);
        Assert.Equal(LayoutCascadeLayer.DomainPackage, envelope.CascadeLayer);
        Assert.True(envelope.Provenance.HasValue);
        Properties(envelope.Provenance.Value, "pack");
        Assert.Equal("general", envelope.Provenance.Value.GetProperty("pack").GetString());
        Assert.Equal("standard", envelope.RetentionClass);
        Assert.True(envelope.LegalHold);
        var requirement = Assert.Single(envelope.Requires);
        Assert.Equal("platform.scheduling", requirement.Capability);
        Assert.Null(requirement.MinimumPlatformVersion);
        Assert.Equal(new DefinitionContractVersion(1, 0), envelope.Contract);
        Assert.Equal("Quarterly", definition.Name);
        Assert.Equal(ScheduleStartKind.Form, definition.Starts.Kind);
        Assert.Equal("forms.inspection", definition.Starts.TargetId);
        Assert.Equal("types.boiler", definition.OnTypeId);
        Assert.Equal("2027-01-01T00:00:00Z", definition.DueDate);
        var window = Assert.IsType<ScheduleWindow>(definition.Window);
        Assert.Equal("2027-01-01T00:00:00Z", window.Earliest);
        Assert.Equal("2027-01-31T00:00:00+02:00", window.Latest);
        Assert.Equal("rules.eu-561@2026", definition.LegalityRef);
        Assert.Equal(ScheduleProblemClass.None, definition.ProblemClass);
        Assert.Null(definition.Basis);
        Assert.Null(definition.Tolerance);
        Assert.Null(definition.Grace);
        Assert.Null(definition.MissedBehaviour);
        Assert.Null(definition.Escalation);
        Assert.Null(definition.Modules);
        Assert.Null(definition.Solver);
        Assert.Null(definition.RecoveryReplanning);
        Assert.Null(definition.RegulatorRuleSetRequirement);
        Assert.Null(definition.InputPlanId);
    }

    private static void Properties(JsonElement element, params string[] expected)
        => Assert.Equal(expected.Order(StringComparer.Ordinal), element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
}
