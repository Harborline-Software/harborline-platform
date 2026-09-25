using System.Text;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-593 item 2: the legacy flat list is replaced by a pinned Layout page surface (ADR 0094).</summary>
public sealed class TemplateSurfaceTests
{
    [Fact(DisplayName = "documents-ck-7: the body is a pinned Layout page surface with intent issue; other media and intents, unresolved or mismatched pins refuse by name")]
    public void TheBodyIsAPinnedPageSurfaceWithIntentIssue()
    {
        Assert.Empty(TemplateDefinitionAdmission.Validate(Template(), Surfaces()));

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.SurfaceMediumUnsupported, "/surface/medium")],
            TemplateDefinitionAdmission.Validate(Template(), Surfaces(Surface(medium: LayoutMedium.Screen, intent: LayoutIntent.Issue))));
        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.SurfaceIntentUnsupported, "/surface/default_intent")],
            TemplateDefinitionAdmission.Validate(Template(), Surfaces(Surface(intent: LayoutIntent.Observe))));
        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.SurfaceUnresolved, "/surface")],
            TemplateDefinitionAdmission.Validate(Template() with { Surface = new(SurfaceId, "2.0.0") }, Surfaces()));

        var moved = Surface() with { Envelope = Surface().Envelope with { Version = "1.1.0" } };
        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.SurfacePinMismatch, "/surface/surface_version")],
            TemplateDefinitionAdmission.Validate(Template(), Surfaces(moved)));
    }

    [Fact(DisplayName = "documents-ck-7: publish consumes Layout's schema-owned numeric ranges through Layout's own validator")]
    public void PublishConsumesLayoutSchemaRanges()
    {
        var range = LayoutDefinitionSchema.Numeric(LayoutNumericMember.Span);
        var blocks = Surface().Blocks.Select((block, index) => index == 0
            ? block with { Placement = new(Span: range.Maximum + 1) }
            : block).ToArray();

        var refusals = TemplateDefinitionAdmission.Validate(Template(), Surfaces(Surface(blocks: blocks)));

        Assert.Equal([new TemplateRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/surface/blocks/0/placement/span")], refusals);
        var refused = Assert.Throws<TemplateAdmissionException>(() => TemplatePack.Export(Template(), Surfaces(Surface(blocks: blocks))));
        Assert.Equal("definition.publish", refused.Stage);
    }

    [Fact(DisplayName = "documents-ck-7: a stored surface holding an invalid value is diagnosed at the persisted read and never clamped")]
    public void PersistedInvalidValueIsDiagnosedNotClamped()
    {
        var range = LayoutDefinitionSchema.Numeric(LayoutNumericMember.Span);
        var stored = JsonNode.Parse(LayoutDefinitionJson.SerializeCanonical(Surface()))!;
        stored["blocks"]![0]!["placement"] = new JsonObject { ["span"] = range.Maximum + 5 };
        var surfaces = Surfaces(stored.ToJsonString());

        var refused = Assert.Throws<TemplateAdmissionException>(() => TemplateDefinitionAdmission.ValidatePersisted(Template(), surfaces));

        Assert.Equal("render.runtime", refused.Stage);
        Assert.Equal([new TemplateRefusal(LayoutDefinitionCodes.NumericOutOfRange, "/surface/blocks/0/placement/span")], refused.Refusals);
    }
}
