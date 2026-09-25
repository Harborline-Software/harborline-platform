using Harborline.Blocks.BuilderDefinitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>
/// Owner ruling Q8 (2026-09-25): publication refuses a Template → pinned Layout surface → Template cycle,
/// walked over the resolved, versioned graph. A template pins a surface (TemplateSurfacePin); a surface block
/// names a template (layout-ck-24), which the host resolves to the version it names. A detached copy adds no edge.
/// </summary>
public sealed class TemplateCycleTests
{
    private static readonly TemplateSurfacePin Other = new("surface.statement", "1.0.0");

    // A page surface whose masthead block names a template (layout-ck-24).
    private static LayoutDefinition Naming(string surfaceId, string templateId) => Surface(blocks:
    [
        new("masthead", "layout.document", new LayoutTemplateBinding(templateId), []),
        new("body", "layout.text", new LayoutStaticBinding(TemplateFixtures.Json("\"Body\"")), []),
    ]) is var surface ? surface with { Envelope = surface.Envelope with { Identity = surfaceId } } : null!;

    [Fact(DisplayName = "documents-q8: a template whose pinned surface names the same template version refuses with composition_cycle at the naming block")]
    public void DirectCycleRefuses()
    {
        var template = Template("2.0.0");
        var surfaces = Surfaces(
            new Dictionary<TemplateSurfacePin, string> { [template.Surface] = Canonical(Naming(SurfaceId, "template.invoice")) },
            id => id == "template.invoice" ? template : null);

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.CompositionCycle, "/surface/blocks/0/binding")],
            TemplateDefinitionAdmission.Validate(template, surfaces));
    }

    [Fact(DisplayName = "documents-q8: a longer cycle through a second template and its surface refuses at the first hop")]
    public void LongerCycleRefuses()
    {
        var invoice = Template("2.0.0");
        var statement = Template("1.0.0") with
        {
            Envelope = Template().Envelope with { Identity = "template.statement" },
            Surface = Other,
        };
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [invoice.Surface] = Canonical(Naming(SurfaceId, "template.statement")),
            [Other] = Canonical(Naming(Other.SurfaceDefinitionId, "template.invoice")),
        }, id => id switch { "template.invoice" => invoice, "template.statement" => statement, _ => null });

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.CompositionCycle, "/surface/blocks/0/binding")],
            TemplateDefinitionAdmission.Validate(invoice, surfaces));
        // A cycle that never returns to the version being published does not refuse it, and the walk terminates.
        var unrelated = Template("3.0.0") with { Envelope = Template().Envelope with { Identity = "template.cover", Version = "3.0.0" } };
        Assert.Empty(TemplateDefinitionAdmission.Validate(unrelated, Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [unrelated.Surface] = Canonical(Naming(SurfaceId, "template.statement")),
            [Other] = Canonical(Naming(Other.SurfaceDefinitionId, "template.statement")),
        }, id => id == "template.statement" ? statement : null)));
    }

    [Fact(DisplayName = "documents-q8: a surface naming a different version of the same template is not a cycle and publishes")]
    public void DifferentVersionIsNotACycle()
    {
        var published = Template("1.0.0") with { Surface = Other };
        var candidate = Template("2.0.0");
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [candidate.Surface] = Canonical(Naming(SurfaceId, "template.invoice")),
            [Other] = Canonical(Surface() with { Envelope = Surface().Envelope with { Identity = Other.SurfaceDefinitionId } }),
        }, id => id == "template.invoice" ? published : null);

        Assert.Empty(TemplateDefinitionAdmission.Validate(candidate, surfaces));
    }

    [Fact(DisplayName = "documents-q8: a detached copy of a surface whose composition is the template adds no edge and publishes")]
    public void DetachedCopyIsNotACycle()
    {
        var template = Template("2.0.0");
        var source = Surface();
        // The template's own surface, detached into an independent draft (Layout's one-way detach). The host records
        // the lineage in provenance; lineage is not a reference edge.
        var copy = LayoutComposition.Detach(
            new LayoutCompositionReference(LayoutCompositionKind.Template, "template.invoice", "2.0.0", SurfaceId, SurfaceVersion),
            source,
            source.Envelope with
            {
                Identity = "surface.invoice-copy",
                Provenance = TemplateFixtures.Json("""{"detached_from":{"composition":"template.invoice","composition_version":"2.0.0","surface":"surface.invoice","surface_version":"1.0.0"}}"""),
            });
        var pinned = template with { Surface = new("surface.invoice-copy", SurfaceVersion) };
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [pinned.Surface] = Canonical(copy),
            [template.Surface] = Canonical(source),
        }, id => id == "template.invoice" ? template : null);

        Assert.Empty(TemplateDefinitionAdmission.Validate(pinned, surfaces));
    }
}
