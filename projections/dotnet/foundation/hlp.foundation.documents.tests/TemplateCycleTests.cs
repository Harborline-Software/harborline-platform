using Harborline.Blocks.BuilderDefinitions;
using System.Text.Json.Nodes;

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
    private static LayoutDefinition Naming(string surfaceId, string templateId, string templateVersion) => Surface(blocks:
    [
        new("masthead", "layout.document", new LayoutTemplateBinding(templateId, templateVersion), []),
        new("body", "layout.text", new LayoutStaticBinding(TemplateFixtures.Json("\"Body\"")), []),
    ]) is var surface ? surface with { Envelope = surface.Envelope with { Identity = surfaceId } } : null!;

    [Fact(DisplayName = "documents-q8: a template whose pinned surface names the same template version refuses with composition_cycle at the naming block")]
    public void DirectCycleRefuses()
    {
        var template = Template("2.0.0");
        var surfaces = Surfaces(
            new Dictionary<TemplateSurfacePin, string> { [template.Surface] = Canonical(Naming(SurfaceId, "template.invoice", "2.0.0")) },
            (id, version) => (id, version) == ("template.invoice", "2.0.0") ? template : null);

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
            [invoice.Surface] = Canonical(Naming(SurfaceId, "template.statement", "1.0.0")),
            [Other] = Canonical(Naming(Other.SurfaceDefinitionId, "template.invoice", "2.0.0")),
        }, (id, version) => (id, version) switch
        {
            ("template.invoice", "2.0.0") => invoice,
            ("template.statement", "1.0.0") => statement,
            _ => null,
        });

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.CompositionCycle, "/surface/blocks/0/binding")],
            TemplateDefinitionAdmission.Validate(invoice, surfaces));
        // A cycle that never returns to the version being published does not refuse it, and the walk terminates.
        var unrelated = Template("3.0.0") with { Envelope = Template().Envelope with { Identity = "template.cover", Version = "3.0.0" } };
        Assert.Empty(TemplateDefinitionAdmission.Validate(unrelated, Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [unrelated.Surface] = Canonical(Naming(SurfaceId, "template.statement", "1.0.0")),
            [Other] = Canonical(Naming(Other.SurfaceDefinitionId, "template.statement", "1.0.0")),
        }, (id, version) => (id, version) == ("template.statement", "1.0.0") ? statement : null)));
    }

    [Fact(DisplayName = "documents-q8: a surface naming a different version of the same template is not a cycle and publishes")]
    public void DifferentVersionIsNotACycle()
    {
        var published = Template("1.0.0") with { Surface = Other };
        var candidate = Template("2.0.0");
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [candidate.Surface] = Canonical(Naming(SurfaceId, "template.invoice", "1.0.0")),
            [Other] = Canonical(Surface() with { Envelope = Surface().Envelope with { Identity = Other.SurfaceDefinitionId } }),
        }, (id, version) => (id, version) == ("template.invoice", "1.0.0") ? published : null);

        Assert.Empty(TemplateDefinitionAdmission.Validate(candidate, surfaces));
    }

    [Fact(DisplayName = "layout-ck-24: a binding pinned to the candidate version refuses even when the host's later version would not close the cycle")]
    public void PinnedVersionClosesCycleIndependentlyOfTheHostsLaterVersion()
    {
        // layout-ck-24: the published binding's version, not a host-selected latest version, is the graph edge.
        var candidate = Template("1.0.0");
        var later = Template("2.0.0") with { Surface = Other };
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [candidate.Surface] = NamingPinned(SurfaceId, "template.invoice", "1.0.0"),
            [Other] = Canonical(Surface() with { Envelope = Surface().Envelope with { Identity = Other.SurfaceDefinitionId } }),
        }, (_, _) => later);

        Assert.Equal(
            [new TemplateRefusal(TemplateDefinitionCodes.CompositionCycle, "/surface/blocks/0/binding")],
            TemplateDefinitionAdmission.Validate(candidate, surfaces));
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
            },
            GrantsAllAuthor.Instance);
        var pinned = template with { Surface = new("surface.invoice-copy", SurfaceVersion) };
        var surfaces = Surfaces(new Dictionary<TemplateSurfacePin, string>
        {
            [pinned.Surface] = Canonical(copy),
            [template.Surface] = Canonical(source),
        }, (id, version) => (id, version) == ("template.invoice", "2.0.0") ? template : null);

        Assert.Empty(TemplateDefinitionAdmission.Validate(pinned, surfaces));
    }

    private static string NamingPinned(string surfaceId, string templateId, string templateVersion)
    {
        var json = JsonNode.Parse(Canonical(Naming(surfaceId, templateId, "ignored")))!.AsObject();
        json["blocks"]![0]!["binding"]!["template_version"] = templateVersion;
        return json.ToJsonString();
    }
}
