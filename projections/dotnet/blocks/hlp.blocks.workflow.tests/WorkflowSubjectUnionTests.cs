using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

using Wire = Harborline.Contracts.Workflow;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// T-488/subject-union. Oracle: ADR 0126 and the independent conformance fixtures.
/// </summary>
public sealed class WorkflowSubjectUnionTests
{
    private static WorkflowDefinition Map(string subject)
    {
        using var document = JsonDocument.Parse($$"""
            {
              {{(subject.Length == 0 ? "" : $"\"subject\": {subject},")}}
              "initialState": "Open",
              "states": [{"id": "Open", "kind": "Normal"}, {"id": "Closed", "kind": "Terminal"}],
              "triggers": [{"id": "approve", "kind": "HumanAction", "task": "approval"}],
              "transitions": [{"id": "t-close", "from": "Open", "on": "approve", "to": "Closed"}],
              "actions": [],
              "guards": []
            }
            """);
        return WorkflowDefinitionWireMapper.ToModel(document.RootElement, "tenant:acme", "invoice-approval", "1.0.0");
    }

    [Fact(DisplayName = "workflows-auth-1: a definition names the record subject it runs on")]
    public void Workflows_auth_1_a_definition_names_the_record_subject_it_runs_on()
    {
        var subject = Map("""{"record": {"recordType": "invoice"}}""").Subject!;

        Assert.Equal(WorkflowSubjectKind.Record, subject.Kind);
        Assert.Equal("invoice", subject.RecordType);
    }

    [Fact(DisplayName = "workflows-auth-12: a set subject names its Record Type")]
    public void Workflows_auth_12_a_set_subject_names_its_record_type()
    {
        var subject = Map("""{"set": {"recordType": "invoice"}}""").Subject!;

        Assert.Equal(WorkflowSubjectKind.Set, subject.Kind);
        Assert.Equal("invoice", subject.RecordType);
    }

    [Fact(DisplayName = "workflows-auth-12: a case subject is refused")]
    public void Workflows_auth_12_a_case_subject_is_refused() =>
        Assert.Throws<ArgumentException>(() => Map("""{"case": {}}"""));

    [Fact(DisplayName = "workflows-ck-12: a definition with no subject maps to none and the legacy form reference is not read")]
    public void Workflows_ck_12_a_definition_with_no_subject_maps_to_none_and_the_legacy_form_reference_is_not_read()
    {
        Assert.Null(Map("").Subject);

        using var legacy = JsonDocument.Parse("""
            {"subjectFormRef": {"formId": "invoice.v1", "version": "1.0.0"}, "initialState": "Open",
             "states": [{"id": "Open", "kind": "Terminal"}], "triggers": [], "transitions": [], "actions": [], "guards": []}
            """);
        Assert.Null(WorkflowDefinitionWireMapper.ToModel(legacy.RootElement, "tenant:acme", "k", "1.0.0").Subject);
    }

    [Theory(DisplayName = "workflows-ck-12: a malformed subject fails closed at the mapper")]
    [InlineData("\"invoice\"")]
    [InlineData("{}")]
    [InlineData("""{"record": {"recordType": "invoice"}, "case": {}}""")]
    [InlineData("""{"record": {"recordType": "invoice"}, "set": {"recordType": "invoice"}}""")]
    [InlineData("""{"record": "invoice"}""")]
    [InlineData("""{"case": "x"}""")]
    [InlineData("""{"record": {}}""")]
    [InlineData("""{"set": {}}""")]
    [InlineData("""{"record": {"recordType": ""}}""")]
    [InlineData("""{"set": {"recordType": 7}}""")]
    [InlineData("""{"Record": {"recordType": "invoice"}}""")]
    public void Workflows_ck_12_a_malformed_subject_fails_closed_at_the_mapper(string subject) =>
        Assert.Throws<ArgumentException>(() => Map(subject));

    [Fact(DisplayName = "workflows-auth-12: only the factories make a subject, and a record or set must name a Record Type")]
    public void Workflows_auth_12_only_the_factories_make_a_subject_and_a_record_or_set_must_name_a_record_type()
    {
        Assert.Empty(typeof(WorkflowSubjectDef).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Throws<ArgumentException>(() => WorkflowSubjectDef.Record(""));
        Assert.Throws<ArgumentNullException>(() => WorkflowSubjectDef.Set(null!));
        Assert.Equal(WorkflowSubjectDef.Record("invoice"), Map("""{"record": {"recordType": "invoice"}}""").Subject);
        Assert.NotEqual(WorkflowSubjectDef.Record("invoice"), WorkflowSubjectDef.Set("invoice"));
    }

    [Fact(DisplayName = "workflows-ck-12: neither Workflow model carries a Forms definition reference")]
    public void Workflows_ck_12_neither_workflow_model_carries_a_forms_definition_reference()
    {
        foreach (var model in new[] { typeof(WorkflowDefinition), typeof(Wire.WorkflowDefinition) })
        {
            var formMembers = model.GetProperties().Where(property =>
                property.Name.Contains("Form", StringComparison.Ordinal)
                || property.PropertyType.ToString().Contains("FormDefinition", StringComparison.Ordinal)).Select(property => property.Name);
            Assert.Empty(formMembers);
        }

        Assert.Null(typeof(WorkflowDefinition).Assembly.GetType("Harborline.Blocks.Workflow.Durable.FormDefinitionRef"));
        Assert.Null(typeof(Wire.WorkflowDefinition).Assembly.GetType("Harborline.Contracts.Workflow.FormDefinitionRef"));
    }

    [Fact(DisplayName = "workflows-ck-12: the engine mapper and the wire reader give the fixture verdict on every shared subject row")]
    public void Workflows_ck_12_the_engine_mapper_and_the_wire_reader_give_the_fixture_verdict_on_every_shared_subject_row()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var root = fixture.RootElement;
        var compared = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            if (!row.TryGetProperty("type", out var type) || type.GetString() != "WorkflowSubject") continue;
            var id = row.GetProperty("id").GetString()!;
            var input = row.GetProperty("input").GetRawText();
            var authored = JsonNode.Parse(root.GetProperty("canonical").GetRawText())!.AsObject();
            authored["subject"] = JsonNode.Parse(input);
            var text = authored.ToJsonString();

            if (row.TryGetProperty("expectedError", out var error))
            {
                var refused = Assert.Throws<Wire.WorkflowWireException>(() => Wire.WorkflowWire.Deserialize("WorkflowDefinition", text));
                Assert.True(refused.Code == error.GetString(), $"{id} wire: {refused.Code}");
                Assert.Throws<ArgumentException>(() => MapAuthored(text));
            }
            else
            {
                var wire = (Wire.WorkflowDefinition)Wire.WorkflowWire.Deserialize("WorkflowDefinition", text);
                var written = JsonNode.Parse(Wire.WorkflowWire.Serialize("WorkflowSubject", wire.Subject.Value!))!;
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(input), written), $"{id} wire wrote {written.ToJsonString()}");

                var kind = JsonNode.Parse(input)!.AsObject().Single();
                var engine = MapAuthored(text).Subject!;
                Assert.Equal(kind.Key, engine.Kind.ToString().ToLowerInvariant());
                Assert.Equal((string?)kind.Value!["recordType"], engine.RecordType);
            }
            compared++;
        }

        Assert.Equal(8, compared);
    }

    [Fact(DisplayName = "workflows-ck-12: the shared canonical definition names a record subject in both models")]
    public void Workflows_ck_12_the_shared_canonical_definition_names_a_record_subject_in_both_models()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FindFixture()));
        var text = fixture.RootElement.GetProperty("canonical").GetRawText();

        Assert.Equal(new Wire.WorkflowRecordSubject("invoice"), ((Wire.WorkflowDefinition)Wire.WorkflowWire.Deserialize("WorkflowDefinition", text)).Subject.Value);
        Assert.Equal(WorkflowSubjectDef.Record("invoice"), MapAuthored(text).Subject);
    }

    private static WorkflowDefinition MapAuthored(string text)
    {
        using var document = JsonDocument.Parse(text);
        return WorkflowDefinitionWireMapper.ToModel(document.RootElement, "tenant:acme", "invoice-approval", "1.0.0");
    }

    private static string FindFixture()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.workflow", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("conformance/hlp.contracts.workflow/fixtures.yaml");
    }
}
