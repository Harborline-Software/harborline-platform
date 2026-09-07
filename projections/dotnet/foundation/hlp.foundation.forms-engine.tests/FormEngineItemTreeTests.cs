using System.Text.Json;
using Contract = Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Exceptions;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FormEngineItemTreeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-03T12:00:00Z");
    private static readonly State.ReusableUnitId AddressUnit = new("catalog/address-block");
    private static readonly State.SemanticVersion V1 = new(1, 0, 0);

    [Fact]
    public async Task RenderAsync_WalksItemTree_ProjectingEveryNodeKindWithDepth()
    {
        var harness = await BuildAsync(TreeForm);

        var section = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections);

        Assert.True(section.Items.HasValue);
        var items = section.Items.Value!;
        Assert.Equal(5, items.Count);
        var top = Assert.IsType<Contract.FieldFormViewItem>(items[0]);
        Assert.Equal("top", top.Key);
        Assert.Equal("top", top.Field.Name);
        var group = Assert.IsType<Contract.GroupFormViewItem>(items[1]);
        Assert.Equal(["g1", "g2"], group.Items.Cast<Contract.FieldFormViewItem>().Select(item => item.Key));
        var collection = Assert.IsType<Contract.CollectionFormViewItem>(items[2]);
        Assert.True(collection.Cardinality.HasValue);
        Assert.Equal(1, collection.Cardinality.Value!.Min);
        Assert.Equal(3, collection.Cardinality.Value.Max.Value);
        Assert.True(collection.Table.HasValue);
        Assert.True(collection.Table.Value!.Columns.HasValue);
        Assert.Contains("c1", collection.Table.Value.Columns.Value!.Keys);
        Assert.True(collection.Table.Value.Totals.HasValue);
        Assert.Contains("c2", collection.Table.Value.Totals.Value!);
        Assert.IsType<Contract.ContentFormViewItem>(items[3]);
        Assert.IsType<Contract.ActionFormViewItem>(items[4]);
    }

    [Fact]
    public async Task RenderAsync_FlatDefinition_LeavesItemsNull()
    {
        var harness = await BuildAsync((schema, tenant) => Form(schema, tenant, ["top"], null));

        var section = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections);

        Assert.False(section.Items.HasValue);
        Assert.Equal(["top"], section.Fields.Select(field => field.Name));
    }

    [Fact]
    public async Task RenderAsync_ReferenceNode_ExpandsToGroupCarryingUnitFields()
    {
        using var units = new InMemoryReusableUnitStore();
        await units.RegisterAsync(AddressBlock(new TenantId("tenant-engine")));
        await units.PublishAsync(new TenantId("tenant-engine"), AddressUnit, V1);
        var harness = await BuildAsync(ReferenceForm, new ReuseResolver(units));

        var items = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections).Items.Value!;

        var reference = Assert.IsType<Contract.GroupFormViewItem>(Assert.Single(items, item => ItemKey(item) == "ref"));
        var premises = Assert.IsType<Contract.GroupFormViewItem>(Assert.Single(reference.Items));
        var address = Assert.IsType<Contract.FieldFormViewItem>(Assert.Single(premises.Items, item => ItemKey(item) == "addressLine1"));
        Assert.Equal("Address line 1", address.Field.Label.Values["en"]);
    }

    [Fact]
    public async Task RenderAsync_DefinitionRedeclaringLockedUnitField_FailsClosed()
    {
        using var units = new InMemoryReusableUnitStore();
        await units.RegisterAsync(AddressBlock(new TenantId("tenant-engine")));
        await units.PublishAsync(new TenantId("tenant-engine"), AddressUnit, V1);
        var harness = await BuildAsync(LockedOverrideForm, new ReuseResolver(units));

        var exception = await Assert.ThrowsAsync<ReuseResolutionException>(async () =>
            await harness.Engine.RenderAsync(harness.Definition.Id, null));

        Assert.Equal(ReusableUnitCodes.LockedFieldOverride, exception.Code);
    }

    [Fact]
    public async Task RenderAsync_ProjectsRuleOutcomes_OntoNestedFieldNodes()
    {
        using var readable = JsonDocument.Parse("""{"flag":"no","detail":"x"}""");
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            schemaJson: """{"type":"object"}""",
            security: new FormEngineOrchestrationTests.RecordingSecurity(readable),
            definitionFactory: RuleTreeForm);
        using var candidate = JsonDocument.Parse("""{"flag":"no","detail":"x"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "tree-rule"));

        var group = Assert.IsType<Contract.GroupFormViewItem>(Assert.Single(
            Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId)).Sections).Items.Value!));
        var detail = Assert.IsType<Contract.FieldFormViewItem>(Assert.Single(group.Items, item => ItemKey(item) == "detail"));

        Assert.True(detail.Field.Rules.HasValue);
        Assert.False(detail.Field.Rules.Value!.Visible);
        Assert.True(detail.Field.IsReadable);
    }

    private static Task<FormEngineOrchestrationTests.Harness> BuildAsync(
        Func<string, TenantId, State.FormDefinition> definitionFactory,
        IReuseResolver? resolver = null) =>
        FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            schemaJson: """{"type":"object"}""",
            definitionFactory: definitionFactory,
            reuseResolver: resolver);

    private static State.FormDefinition TreeForm(string schema, TenantId tenant) => Form(
        schema,
        tenant,
        ["top", "g1", "g2", "c1", "c2"],
        [
            State.FormItem.OfField("top"),
            State.FormItem.OfGroup("grp", [State.FormItem.OfField("g1"), State.FormItem.OfField("g2")], Text("Group")),
            State.FormItem.OfCollection("coll", [State.FormItem.OfField("c1"), State.FormItem.OfField("c2")], new(1, 3), Text("Coll")) with
            {
                Table = new State.CollectionTableConfig(
                    new Dictionary<string, State.CollectionColumn> { ["c1"] = new(Width: "1/2") },
                    ["c2"]),
            },
            State.FormItem.OfContent("intro", [
                new(State.ContentNodeKinds.Heading, Text("Heading"), Level: 3),
                new(State.ContentNodeKinds.Paragraph, Text("Body")),
            ]),
            State.FormItem.OfAction("act", new(State.FormActionKinds.OpenUrl, Text("Open"), Url: "https://example.harborline-software.com")),
        ]);

    private static State.FormDefinition ReferenceForm(string schema, TenantId tenant) => Form(
        schema,
        tenant,
        ["top"],
        [State.FormItem.OfField("top"), State.FormItem.OfReference("ref", new(AddressUnit, State.ReusableUnitVersionSelector.Pin(V1)))]);

    private static State.FormDefinition LockedOverrideForm(string schema, TenantId tenant) => Form(
        schema,
        tenant,
        ["top", "addressLine1"],
        [State.FormItem.OfField("top"), State.FormItem.OfReference("ref", new(AddressUnit, State.ReusableUnitVersionSelector.Pin(V1)))]);

    private static State.FormDefinition RuleTreeForm(string schema, TenantId tenant) => Form(
        schema,
        tenant,
        ["flag", "detail"],
        [State.FormItem.OfGroup("grp", [State.FormItem.OfField("flag"), State.FormItem.OfField("detail")], Text("Group"))],
        [new("vis.detail", State.RuleTier.JsonLogic, State.RuleScope.Field, "detail", "{\"==\":[{\"var\":\"flag\"},\"yes\"]}", State.RuleActionKind.Visibility)]);

    private static State.FormDefinition Form(
        string schema,
        TenantId tenant,
        IReadOnlyList<string> fields,
        IReadOnlyList<State.FormItem>? items,
        IReadOnlyList<State.RuleDefinition>? rules = null) => new(
            new("treeform"),
            V1,
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schema),
            new(
                fields.ToDictionary(field => field, field => new State.FieldOverlay(Text(field)), StringComparer.Ordinal),
                [new("main", Text("Main"), fields, new([Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("reader")]), Items: items)],
                rules ?? []),
            null,
            Now,
            Now);

    private static State.ReusableUnit AddressBlock(TenantId tenant) => new(
        AddressUnit,
        V1,
        State.FormDefinitionStatus.Draft,
        tenant,
        State.ReusableUnitKind.FormComponent,
        State.IdentityRef.System,
        new(
            [State.FormItem.OfGroup("premises", [State.FormItem.OfField("addressLine1"), State.FormItem.OfField("addressCity")], Text("Premises"))],
            new Dictionary<string, State.FieldOverlay>
            {
                ["addressLine1"] = new(Text("Address line 1"), ControlHint: "text"),
                ["addressCity"] = new(Text("City"), ControlHint: "text"),
            }),
        Text("Address block"),
        Now,
        Now);

    private static State.InternationalizedText Text(string value) => State.InternationalizedText.FromInvariant(value);

    private static string ItemKey(Contract.FormViewItem item) => item switch
    {
        Contract.FieldFormViewItem field => field.Key,
        Contract.GroupFormViewItem group => group.Key,
        Contract.CollectionFormViewItem collection => collection.Key,
        Contract.ContentFormViewItem content => content.Key,
        Contract.ActionFormViewItem action => action.Key,
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };
}
