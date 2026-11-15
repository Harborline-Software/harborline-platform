using System.Text;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assistance;
using Xunit;

namespace Harborline.Foundation.Assistance.Tests;

// T-610/allowlist-field-classification. Oracle for every expected code and pointer: ADR-0146 decisions 1-4
// and the slice record's Build steps 2-4 (literals), DES-0026 pilot-auth-10 and pilot-ck-11, DES-0015 §2.
public sealed class FieldClassificationTests
{
    private static readonly Harborline.Foundation.Definitions.DefinitionContractWindow ContractWindow = new(1, 0, 1);
    private const string Tenant = "tenant-a";
    private const string Key = "assistance.customer";
    private static readonly AssistanceAdmissionPhase[] EveryPhase = [AssistanceAdmissionPhase.Author, AssistanceAdmissionPhase.Publish, AssistanceAdmissionPhase.Install];
    private static readonly AssistanceAdmissionPhase[] PublishAndInstall = [AssistanceAdmissionPhase.Publish, AssistanceAdmissionPhase.Install];

    [Theory(DisplayName = "pilot-auth-10: a value entry on a personal, confidential or masked field refuses definition.context_field_value_forbidden at /context_allowlist/{i}/redaction at author, publish and install")]
    [InlineData("email")]
    [InlineData("credit_limit")]
    [InlineData("tax_id")]
    public void Value_entry_on_a_governed_field_refuses(string field)
    {
        var definition = Definition(Entry("status", AssistanceContextRedaction.Value), Entry(field, AssistanceContextRedaction.Value));
        foreach (var phase in EveryPhase)
        {
            Assert.Equal([("definition.context_field_value_forbidden", "/context_allowlist/1/redaction")], Refusals(definition, phase, new FixtureFieldGovernance()));
            Assert.Equal([("definition.context_field_value_forbidden", "/context_allowlist/1/redaction")], JsonRefusals(definition, phase, new FixtureFieldGovernance()));
        }
    }

    [Theory(DisplayName = "pilot-auth-10: a presence or bucket entry on a personal, confidential or masked field admits at author, publish and install")]
    [InlineData("email", AssistanceContextRedaction.Presence)]
    [InlineData("email", AssistanceContextRedaction.Bucket)]
    [InlineData("credit_limit", AssistanceContextRedaction.Presence)]
    [InlineData("credit_limit", AssistanceContextRedaction.Bucket)]
    [InlineData("tax_id", AssistanceContextRedaction.Presence)]
    [InlineData("tax_id", AssistanceContextRedaction.Bucket)]
    public void Presence_or_bucket_entry_on_a_governed_field_admits(string field, AssistanceContextRedaction redaction)
    {
        var definition = Definition(Entry(field, redaction));
        foreach (var phase in EveryPhase)
        {
            Assert.Empty(Refusals(definition, phase, new FixtureFieldGovernance()));
            Assert.Empty(JsonRefusals(definition, phase, new FixtureFieldGovernance()));
        }
    }

    [Fact(DisplayName = "pilot-auth-10: a value entry on an ungoverned field admits at author, publish and install")]
    public void Value_entry_on_an_ungoverned_field_admits()
    {
        var definition = Definition(Entry("status", AssistanceContextRedaction.Value));
        foreach (var phase in EveryPhase) Assert.Empty(Refusals(definition, phase, new FixtureFieldGovernance()));
        AssistanceDefinitionPackExporter.Export(definition, ContractWindow, fields: new FixtureFieldGovernance());
    }

    [Theory(DisplayName = "pilot-ck-11: a field the governance port does not know refuses definition.context_field_unknown at /context_allowlist/{i}, whatever its redaction, at author, publish and install")]
    [InlineData(AssistanceContextRedaction.Value)]
    [InlineData(AssistanceContextRedaction.Presence)]
    [InlineData(AssistanceContextRedaction.Bucket)]
    public void Unknown_field_refuses(AssistanceContextRedaction redaction)
    {
        var definition = Definition(Entry("status", AssistanceContextRedaction.Value), Entry("nickname", redaction));
        foreach (var phase in EveryPhase)
        {
            Assert.Equal([("definition.context_field_unknown", "/context_allowlist/1")], Refusals(definition, phase, new FixtureFieldGovernance()));
            Assert.Equal([("definition.context_field_unknown", "/context_allowlist/1")], JsonRefusals(definition, phase, new FixtureFieldGovernance()));
        }
    }

    [Fact(DisplayName = "pilot-ck-11: without a governance port, publish and install refuse every allowlist entry with definition.context_field_unknown and author skips the check")]
    public void Without_a_port_publish_and_install_refuse_every_entry_and_author_skips()
    {
        var definition = Definition(Entry("status", AssistanceContextRedaction.Value), Entry("email", AssistanceContextRedaction.Presence));
        foreach (var phase in PublishAndInstall)
        {
            Assert.Equal([("definition.context_field_unknown", "/context_allowlist/0"), ("definition.context_field_unknown", "/context_allowlist/1")], Refusals(definition, phase, null));
            Assert.Equal([("definition.context_field_unknown", "/context_allowlist/0"), ("definition.context_field_unknown", "/context_allowlist/1")], JsonRefusals(definition, phase, null));
        }
        Assert.Empty(Refusals(definition, AssistanceAdmissionPhase.Author, null));
        Assert.Empty(JsonRefusals(definition, AssistanceAdmissionPhase.Author, null));
        Assert.Equal([("definition.context_field_unknown", "/context_allowlist/0"), ("definition.context_field_unknown", "/context_allowlist/1")],
            Assert.Throws<AssistanceAdmissionException>(() => AssistanceDefinitionPackExporter.Export(definition, ContractWindow)).Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
    }

    [Fact(DisplayName = "pilot-auth-10 pilot-ck-11: AdmitJson reports both field refusals with the rest of the refusals in one list")]
    public void AdmitJson_reports_field_refusals_with_the_rest()
    {
        var definition = Definition(Entry("nickname", AssistanceContextRedaction.Value), Entry("email", AssistanceContextRedaction.Value)) with { Surface = "" };
        var body = Encoding.UTF8.GetString(AssistanceDefinitionJson.SerializeCanonical(definition));
        var refusals = AssistanceDefinitionAdmission.AdmitJson(body, AssistanceAdmissionPhase.Install, ContractWindow, catalogue: new("tenant-b", Key, "1.0.0"), fields: new FixtureFieldGovernance());
        Assert.Equal(
            [("definition.surface_required", "/surface"), ("definition.context_field_unknown", "/context_allowlist/0"), ("definition.context_field_value_forbidden", "/context_allowlist/1/redaction"), ("definition.catalogue_mismatch", "/tenant")],
            refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.Equal(
            [("definition.surface_required", "/surface"), ("definition.context_field_unknown", "/context_allowlist/0"), ("definition.context_field_value_forbidden", "/context_allowlist/1/redaction")],
            Assert.Throws<AssistanceAdmissionException>(() => AssistanceDefinitionAdmission.Require(definition, AssistanceAdmissionPhase.Install, ContractWindow, fields: new FixtureFieldGovernance())).Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
    }

    [Theory(DisplayName = "pilot-auth-10 (DES-0015 §2, classification masks and never grants): a classified personal, confidential or masked field still refuses a value entry, whatever its classification id")]
    [InlineData(true, false, false, "public")]
    [InlineData(false, true, false, "public")]
    [InlineData(false, false, true, "public")]
    [InlineData(true, true, true, "unrestricted")]
    public void Classification_never_grants_a_value_entry(bool personal, bool confidential, bool masked, string classification)
    {
        var fields = new FixtureFieldGovernance(("records.customer", "classified", new FieldGovernanceDefinition(personal, confidential, masked, classification)));
        var definition = Definition(Entry("classified", AssistanceContextRedaction.Value));
        foreach (var phase in EveryPhase) Assert.Equal([("definition.context_field_value_forbidden", "/context_allowlist/0/redaction")], Refusals(definition, phase, fields));
    }

    [Fact(DisplayName = "pilot-ck-11 (DES-0015 §2, classification masks and never grants): a governance port that classifies every field admits no entry the allowlist refuses, and a blank field stays unknown")]
    public void Classification_never_admits_an_entry_the_allowlist_refuses()
    {
        var everything = new ClassifyEverything();
        var definition = Definition(new("records.customer", "", AssistanceContextRedaction.Presence), new("", "status", AssistanceContextRedaction.Bucket));
        foreach (var phase in EveryPhase)
            Assert.Equal(
                [("definition.context_field_required", "/context_allowlist/0/field"), ("definition.context_field_unknown", "/context_allowlist/0"),
                 ("definition.context_definition_required", "/context_allowlist/1/definition"), ("definition.context_field_unknown", "/context_allowlist/1")],
                Refusals(definition, phase, everything));
        Assert.Equal(0, everything.Calls);
    }

    private static (string, string)[] Refusals(AssistanceDefinition definition, AssistanceAdmissionPhase phase, IAssistanceFieldGovernance? fields)
        => AssistanceDefinitionAdmission.Validate(definition, phase, ContractWindow, fields: fields).Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static (string, string)[] JsonRefusals(AssistanceDefinition definition, AssistanceAdmissionPhase phase, IAssistanceFieldGovernance? fields)
        => AssistanceDefinitionAdmission.AdmitJson(Encoding.UTF8.GetString(AssistanceDefinitionJson.SerializeCanonical(definition)), phase, ContractWindow, fields: fields).Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static AssistanceContextAllowlistEntry Entry(string field, AssistanceContextRedaction redaction) => new("records.customer", field, redaction);

    private static AssistanceDefinition Definition(params AssistanceContextAllowlistEntry[] allowlist) => new(
        Tenant, Key, "1.0.0", "customer", "/customers/{customerId}", [], allowlist, ["support"], new("provider-a", "model-a"),
        Envelope: new(Key, "1.0.0", Tenant, AssistanceCascadeLayer.Tenant, JsonDocument.Parse("{\"source\":\"tenant\"}").RootElement.Clone(), "restricted", false, [], new(1, 0)));

    private sealed class ClassifyEverything : IAssistanceFieldGovernance
    {
        public int Calls { get; private set; }
        public FieldGovernanceDefinition? Resolve(string definition, string field)
        {
            Calls++;
            return new(false, false, false, "public");
        }
    }
}
