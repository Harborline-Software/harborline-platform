using System.Text;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-593 item 3: canonical authored content is validated before publish; one bad item never bricks an install.</summary>
public sealed class TemplatePackTests
{
    [Fact(DisplayName = "documents-eng-19: a pack content item parses into a TemplateDefinition against the pinned canonical contract, and a malformed item is a named miss, never an exception")]
    public void ContentItemParsesOrIsANamedMiss()
    {
        var entry = TemplatePack.Export(Template(), Surfaces());
        Assert.True(TemplatePack.TryParse(entry.Content, Tenant, Provenance, out var parsed, out var none));
        Assert.Null(none);
        Assert.Equal(TemplateDefinitionJson.SerializeCanonical(Template()), TemplateDefinitionJson.SerializeCanonical(parsed!));

        var unknownMember = JsonNode.Parse(entry.Content)!;
        unknownMember["structure"] = new JsonArray();
        foreach (var malformed in new[] { "not json", "[]", "{}", """{"envelope":{}}""", unknownMember.ToJsonString() })
        {
            Assert.False(TemplatePack.TryParse(Encoding.UTF8.GetBytes(malformed), Tenant, Provenance, out var template, out var miss));
            Assert.Null(template);
            Assert.Equal(TemplateDefinitionCodes.BodyInvalid, miss!.Code);
        }
    }

    [Theory(DisplayName = "documents-auth-18: a server-derived authority field inside authored content refuses by name")]
    [InlineData("/owner")]
    [InlineData("/tenant")]
    [InlineData("/pack_key")]
    [InlineData("/provenance")]
    [InlineData("/envelope/owner")]
    [InlineData("/envelope/tenant")]
    [InlineData("/envelope/pack_key")]
    [InlineData("/envelope/provenance")]
    public void AuthorityFieldInAuthoredContentRefusesByName(string pointer)
    {
        var content = JsonNode.Parse(TemplatePack.Export(Template(), Surfaces()).Content)!.AsObject();
        var parent = pointer.StartsWith("/envelope/", StringComparison.Ordinal) ? content["envelope"]!.AsObject() : content;
        parent[pointer[(pointer.LastIndexOf('/') + 1)..]] = "self-declared";

        Assert.False(TemplatePack.TryParse(Encoding.UTF8.GetBytes(content.ToJsonString()), Tenant, Provenance, out var template, out var miss));

        Assert.Null(template);
        Assert.Equal(new TemplateRefusal(TemplateDefinitionCodes.AuthorityFieldForbidden, pointer), miss);
    }

    [Fact(DisplayName = "documents-auth-19: a repeating region declared with no columns refuses rather than rendering an empty table")]
    public void RepeatingRegionWithoutColumnsRefuses()
    {
        var blocks = Surface().Blocks.Select(block => block.Repeating ? block with { Children = [] } : block).ToArray();

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.RepeatingRegionColumnsRequired, "/surface/blocks/1/children")],
            TemplateDefinitionAdmission.Validate(Template(), Surfaces(Surface(blocks: blocks))));
        Assert.Empty(TemplateDefinitionAdmission.Validate(Template(), Surfaces()));
    }

    [Fact(DisplayName = "documents-auth-20: a malformed template body is a named miss that skips one item and installs its valid sibling")]
    public async Task MalformedItemIsSkippedAndItsSiblingInstalls()
    {
        var good = TemplatePack.Export(Template(), Surfaces());
        var bad = new TemplatePackEntry("template.broken", "1.0.0", Encoding.UTF8.GetBytes("{\"envelope\":"));
        var published = new List<TemplateDefinition>();

        var outcomes = await TemplatePack.InstallAsync([bad, good], Target(published));

        Assert.Equal(
            [(TemplateInstallOutcomeKind.Refused, TemplateDefinitionCodes.BodyInvalid), (TemplateInstallOutcomeKind.Published, (string?)null)],
            outcomes.Select(outcome => (outcome.Kind, outcome.Refusals.FirstOrDefault()?.Code)));
        Assert.Equal("template.broken", outcomes[0].DefinitionId);
        Assert.Equal("template.invoice", Assert.Single(published).Envelope.Identity);
    }

    private static TemplateInstallTarget Target(List<TemplateDefinition> published) => new(
        Tenant, Provenance, Surfaces(),
        _ => ValueTask.FromResult<byte[]?>(null),
        template => { published.Add(template); return ValueTask.CompletedTask; });
}
