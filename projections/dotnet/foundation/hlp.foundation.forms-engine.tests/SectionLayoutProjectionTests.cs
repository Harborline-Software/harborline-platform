using Contract = Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SectionLayoutProjectionTests
{
    [Fact]
    public async Task RenderAsync_GridLayout_ProjectsLayoutAndPlacementOntoView()
    {
        var layout = new State.SectionLayout(State.SectionLayoutKind.Grid, Columns: 3, Gap: 6);
        var placement = new Dictionary<string, State.FieldPlacement>(StringComparer.Ordinal)
        {
            ["addressLine1"] = new(ColSpan: 2),
            ["city"] = new(ColSpan: 1),
        };
        var harness = await BuildAsync(layout, placement);

        var section = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections);

        Assert.True(section.Layout.HasValue);
        var projectedLayout = Assert.IsType<Contract.SectionLayout>(section.Layout.Value);
        Assert.Equal(Contract.SectionLayoutKind.Grid, projectedLayout.Kind);
        Assert.Equal(3, projectedLayout.Columns);
        Assert.Equal(new Contract.LayoutGap(6), projectedLayout.Gap);
        Assert.True(section.FieldPlacement.HasValue);
        var projectedPlacement = Assert.IsAssignableFrom<IReadOnlyDictionary<string, Contract.FieldPlacement>>(section.FieldPlacement.Value);
        Assert.Equal(2, projectedPlacement["addressLine1"].ColSpan);
        Assert.Equal(1, projectedPlacement["city"].ColSpan);
    }

    [Fact]
    public async Task RenderAsync_FlexLayout_ProjectsDirectionWrapAndGrow()
    {
        var layout = new State.SectionLayout(
            State.SectionLayoutKind.Flex,
            Direction: State.FlexDirection.Row,
            Wrap: State.FlexWrap.Wrap,
            Gap: 4);
        var placement = new Dictionary<string, State.FieldPlacement>(StringComparer.Ordinal)
        {
            ["addressLine1"] = new(Grow: 1),
        };
        var harness = await BuildAsync(layout, placement);

        var section = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections);

        Assert.True(section.Layout.HasValue);
        var projectedLayout = Assert.IsType<Contract.SectionLayout>(section.Layout.Value);
        Assert.Equal(Contract.SectionLayoutKind.Flex, projectedLayout.Kind);
        Assert.Equal(Contract.FlexDirection.Row, projectedLayout.Direction);
        Assert.Equal(Contract.FlexWrap.Wrap, projectedLayout.Wrap);
        Assert.True(section.FieldPlacement.HasValue);
        var projectedPlacement = Assert.IsAssignableFrom<IReadOnlyDictionary<string, Contract.FieldPlacement>>(section.FieldPlacement.Value);
        Assert.Equal(1, projectedPlacement["addressLine1"].Grow);
    }

    [Fact]
    public async Task RenderAsync_NoLayout_ProjectsAbsentLayout_BackCompatStack()
    {
        var harness = await BuildAsync(null, null);

        var section = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections);

        Assert.False(section.Layout.HasValue);
        Assert.False(section.FieldPlacement.HasValue);
    }

    [Fact]
    public async Task RenderAsync_LayoutDoesNotAlterReadability_PresentationOnly()
    {
        var withLayout = await BuildAsync(new State.SectionLayout(State.SectionLayoutKind.Grid, Columns: 2), null);
        var withoutLayout = await BuildAsync(null, null);

        var first = (await withLayout.Engine.RenderAsync(withLayout.Definition.Id, null)).Sections.Single().Fields
            .Select(field => (field.Name, field.IsReadable)).OrderBy(field => field.Name).ToArray();
        var second = (await withoutLayout.Engine.RenderAsync(withoutLayout.Definition.Id, null)).Sections.Single().Fields
            .Select(field => (field.Name, field.IsReadable)).OrderBy(field => field.Name).ToArray();

        Assert.Equal(second, first);
    }

    private static Task<FormEngineOrchestrationTests.Harness> BuildAsync(
        State.SectionLayout? layout,
        IReadOnlyDictionary<string, State.FieldPlacement>? placement) =>
        FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            definitionFactory: (schemaId, tenant) => Definition(schemaId, tenant, layout, placement));

    private static State.FormDefinition Definition(
        string schemaId,
        TenantId tenant,
        State.SectionLayout? layout,
        IReadOnlyDictionary<string, State.FieldPlacement>? placement)
    {
        var fields = new Dictionary<string, State.FieldOverlay>(StringComparer.Ordinal)
        {
            ["addressLine1"] = new(State.InternationalizedText.FromInvariant("Address line 1")),
            ["city"] = new(State.InternationalizedText.FromInvariant("City")),
        };
        var now = DateTimeOffset.Parse("2026-06-27T12:00:00Z");
        return new(
            new("inspection"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schemaId),
            new(
                fields,
                [new(
                    "location",
                    State.InternationalizedText.FromInvariant("Location"),
                    ["addressLine1", "city"],
                    new([Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("writer")]),
                    Layout: layout,
                    FieldPlacement: placement)],
                []),
            null,
            now,
            now);
    }
}
