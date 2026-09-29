using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-593: the Documents definition producer, content kind 7 (DES-0021 section 2; T-738).</summary>
public sealed class TemplateDefinitionTests
{
    [Fact(DisplayName = "documents-ck-1: TemplateDefinition travels as content kind 7 (the api's PackContentKind.TemplateDefinition, T-738) and round-trips through canonical JSON and export with identical pin and digest")]
    public void TemplateDefinitionIsContentKindSevenAndRoundTrips()
    {
        var template = Template();

        var entry = TemplatePack.Export(template, Surfaces());
        Assert.Equal(7, entry.ContentKind); // 6 is the api's TerminologyOverride
        Assert.Equal("template.invoice", entry.DefinitionId);
        Assert.Equal("1.0.0", entry.Version);

        var canonical = TemplateDefinitionJson.SerializeCanonical(template);
        var reread = TemplateDefinitionJson.Deserialize(canonical);
        Assert.Equal(canonical, TemplateDefinitionJson.SerializeCanonical(reread));
        Assert.Equal(template.Surface, reread.Surface);

        Assert.True(TemplatePack.TryParse(entry.Content, Tenant, Provenance, out var imported, out var miss), miss?.Code);
        var again = TemplatePack.Export(imported!, Surfaces());
        Assert.Equal(entry.Digest, again.Digest);
        Assert.Equal(template.Surface, imported!.Surface);
        Assert.Equal(canonical, TemplateDefinitionJson.SerializeCanonical(imported));
    }

    [Fact(DisplayName = "T-572 slice 2: the envelope contract survives canonical serialize and deserialize, and TemplatePack.Export then TryParse")]
    public void EnvelopeContractSurvivesCanonicalRoundTripAndPackExport()
    {
        var canonical = TemplateDefinitionJson.SerializeCanonical(Template());
        var parsed = TemplateDefinitionJson.Deserialize(WithContract(canonical));
        Assert.Equal("""{"major":1,"minor":0}""", ContractOf(TemplateDefinitionJson.SerializeCanonical(parsed)));

        var entry = TemplatePack.Export(parsed, Surfaces());
        Assert.True(TemplatePack.TryParse(entry.Content, Tenant, Provenance, out var imported, out var miss), miss?.Code);
        Assert.Equal("""{"major":1,"minor":0}""", ContractOf(TemplateDefinitionJson.SerializeCanonical(imported!)));

        // Slice 2 changes no admission outcome: a template without the member still parses (slice 3 refuses it).
        Assert.Null(ContractOf(TemplateDefinitionJson.SerializeCanonical(TemplateDefinitionJson.Deserialize(WithContract(canonical, present: false)))));
    }

    private static byte[] WithContract(byte[] canonical, bool present = true)
    {
        var node = JsonNode.Parse(canonical)!;
        var envelope = node["envelope"]!.AsObject();
        envelope.Remove("contract");
        if (present) envelope["contract"] = new JsonObject { ["major"] = 1, ["minor"] = 0 };
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static string? ContractOf(byte[] json) => JsonNode.Parse(json)!["envelope"]!["contract"]?.ToJsonString();

    [Fact(DisplayName = "documents-ck-2: the envelope carries identity, version, tenant, cascade layer, provenance and requires, and each missing member refuses by name")]
    public void EnvelopeMembersRoundTripAndEachMissingMemberRefuses()
    {
        var template = Template();
        var node = JsonNode.Parse(TemplateDefinitionJson.SerializeCanonical(template))!;
        Assert.Equal(
            ["cascade_layer", "contract", "identity", "provenance", "requires", "tenant", "version"],
            node["envelope"]!.AsObject().Select(member => member.Key));
        Assert.Empty(TemplateDefinitionAdmission.Validate(template, Surfaces()));

        var envelope = template.Envelope;
        foreach (var (broken, pointer) in new (TemplateDefinitionEnvelope, string)[]
        {
            (envelope with { Identity = " " }, "/envelope/identity"),
            (envelope with { Version = "" }, "/envelope/version"),
            (envelope with { Tenant = "" }, "/envelope/tenant"),
            (envelope with { CascadeLayer = (TemplateCascadeLayer)99 }, "/envelope/cascade_layer"),
            (envelope with { Provenance = default }, "/envelope/provenance"),
            (envelope with { Requires = null! }, "/envelope/requires"),
            (envelope with { Requires = [new(" ")] }, "/envelope/requires/0/capability"),
        })
        {
            var refusal = Assert.Single(TemplateDefinitionAdmission.Validate(template with { Envelope = broken }, Surfaces()));
            Assert.Equal(new DefinitionRefusal(TemplateDefinitionCodes.EnvelopeInvalid, pointer), refusal);
        }
    }

    [Fact(DisplayName = "documents-ck-3: DocumentType is the open discriminator a template issues under; it survives export and a blank one refuses")]
    public void DocumentTypeIsRequiredAndSurvivesExport()
    {
        var template = Template() with { DocumentType = "credit-note" };
        var entry = TemplatePack.Export(template, Surfaces());
        Assert.True(TemplatePack.TryParse(entry.Content, Tenant, Provenance, out var imported, out _));
        Assert.Equal("credit-note", imported!.DocumentType);

        var refusal = Assert.Single(TemplateDefinitionAdmission.Validate(template with { DocumentType = " " }, Surfaces()));
        Assert.Equal(new DefinitionRefusal(TemplateDefinitionCodes.DocumentTypeRequired, "/document_type"), refusal);
        Assert.Throws<DefinitionRefusalException>(() => TemplatePack.Export(template with { DocumentType = "" }, Surfaces()));
    }
}
