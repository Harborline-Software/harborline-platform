using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-593: the Documents definition producer, content kind 6 (DES-0021 section 2).</summary>
public sealed class TemplateDefinitionTests
{
    [Fact(DisplayName = "documents-ck-1: TemplateDefinition travels as content kind 6 and round-trips through canonical JSON and export with identical pin and digest")]
    public void TemplateDefinitionIsContentKindSixAndRoundTrips()
    {
        var template = Template();

        var entry = TemplatePack.Export(template, Surfaces());
        Assert.Equal(6, entry.ContentKind);
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

    [Fact(DisplayName = "documents-ck-2: the envelope carries identity, version, tenant, cascade layer, provenance and requires, and each missing member refuses by name")]
    public void EnvelopeMembersRoundTripAndEachMissingMemberRefuses()
    {
        var template = Template();
        var node = JsonNode.Parse(TemplateDefinitionJson.SerializeCanonical(template))!;
        Assert.Equal(
            ["cascade_layer", "identity", "provenance", "requires", "tenant", "version"],
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
            Assert.Equal(new TemplateRefusal(TemplateDefinitionCodes.EnvelopeInvalid, pointer), refusal);
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
        Assert.Equal(new TemplateRefusal(TemplateDefinitionCodes.DocumentTypeRequired, "/document_type"), refusal);
        Assert.Throws<TemplateAdmissionException>(() => TemplatePack.Export(template with { DocumentType = "" }, Surfaces()));
    }
}
