using System.Text;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>
/// Owner ruling Q6: the Layout tree replaces the legacy block list, so documents-ck-8 and documents-ck-9 retire.
/// These parity tests keep the behaviour each protected: a flat, ordered, canonically round-tripping body of
/// the five legacy roles (header, section, field grid, repeating region, footer), rendered in authored order.
/// </summary>
public sealed class LegacyBlockParityTests
{
    // The legacy invoice's five roles as a depth-one page surface with intent issue (ADR 0094 decision 1).
    private static LayoutDefinition FiveRoles()
    {
        LayoutBlock Text(string id, string text, LayoutFlowRole role = LayoutFlowRole.Flow, string? region = null)
            => new(id, "layout.text", new LayoutStaticBinding(TemplateFixtures.Json($"\"{text}\"")), [], FlowRole: role, StaticRegion: region);
        LayoutBlock[] blocks =
        [
            Text("header", "Invoice", LayoutFlowRole.Static, "masthead"),
            Text("section", "Thank you for your business."),
            new("field-grid", "layout.stack", new LayoutStaticBinding(TemplateFixtures.Json("\"Bill to\"")),
                [Text("grid-label", "Customer"), Text("grid-value", "Acme")], Container: new(LayoutContainerKind.Stack, ColumnCount: 2)),
            new("repeating-region", "layout.table", new LayoutQueryBinding("view.invoice-lines"),
                [Text("column-description", "Description"), Text("column-amount", "Amount")],
                Container: new(LayoutContainerKind.Stack), Repeating: true),
            Text("footer", "Terms: net 30", LayoutFlowRole.Static, "terms"),
        ];
        var flow = blocks.Where(block => block.FlowRole == LayoutFlowRole.Flow).Select(block => block.Id).ToArray();
        return Surface(blocks: blocks) with
        {
            PageMasters = [new("default", "a4", new("masthead", null, "terms"), new("masthead", null, "terms"), new("masthead", null, "terms"))],
            PageRuns = [new("body", "a4", "default", flow)],
        };
    }

    [Fact(DisplayName = "documents-ck-8 (retired, parity): the depth-one body of the five legacy roles round-trips through canonical JSON with its order and the template's pin intact")]
    public void FlatBodyRoundTripsThroughCanonicalJson()
    {
        var surface = FiveRoles();
        var canonical = LayoutDefinitionJson.SerializeCanonical(surface);
        var reread = LayoutDefinitionJson.Deserialize(canonical);

        Assert.Equal(canonical, LayoutDefinitionJson.SerializeCanonical(reread));
        Assert.Equal(["header", "section", "field-grid", "repeating-region", "footer"], reread.Blocks.Select(block => block.Id));
        Assert.Empty(TemplateDefinitionAdmission.Validate(Template(), Surfaces(Encoding.UTF8.GetString(canonical))));
        Assert.True(TemplatePack.TryParse(TemplatePack.Export(Template(), Surfaces(surface)).Content, Tenant, Provenance, out var imported, out _));
        Assert.Equal(Template().Surface, imported!.Surface);
    }

    [Fact(DisplayName = "documents-ck-9 (retired, parity): the five legacy roles admit on a page surface and render in authored order, header and footer in the page's static regions")]
    public void FiveLegacyRolesRenderInOrder()
    {
        var surface = FiveRoles();
        Assert.Empty(TemplateDefinitionAdmission.Validate(Template(), Surfaces(surface)));

        var plan = new LayoutRuntimeEngine().Flow(surface);
        var page = Assert.Single(plan.PageFragments!);

        Assert.Equal(
            ["section", "field-grid", "grid-label", "grid-value", "repeating-region", "column-description", "column-amount"],
            plan.Flow.Select(block => block.BlockId));
        Assert.Equal(["section", "field-grid", "repeating-region"], page.Flow.Select(block => block.BlockId));
        Assert.Equal(["header", "footer"], page.StaticRegions.Select(block => block.BlockId));
    }
}
