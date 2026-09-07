using System.Text.Json;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// F-23 — layout breadth: static content blocks, action blocks, and intent-based
/// responsive zone layout. Tests that a definition carrying blocks + zone intents
/// round-trips through the store AND through System.Text.Json web defaults (the
/// durable path) intact; that a blockless/zoneless definition is unchanged
/// (back-compat — the new members stay null); and that the fail-closed admission
/// invariants REJECT malformed blocks/intents with stable, localizable codes
/// (unknown node/action kinds, empty payloads, non-http(s) URLs, undeclared scroll
/// targets, unknown intent tokens, zone intents on a non-Group item).
/// </summary>
public sealed class FormLayoutBreadthTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ── round-trip + back-compat ───────────────────────────────────────────────

    [Fact]
    public async Task Definition_with_blocks_and_zones_round_trips_through_the_store_intact()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var def = LayoutBreadthForm();

        await store.RegisterAsync(def); // exercises the fail-closed F-23 invariants
        var loaded = await store.GetAsync(Tenant, def.Id, def.Version);

        var items = loaded.Overlay.Sections[0].Items!;
        Assert.Equal(FormItemKind.Content, items[0].Kind);
        Assert.Equal(2, items[0].Content!.Count);
        Assert.Equal(ContentNodeKinds.Heading, items[0].Content![0].Kind);
        Assert.Equal(3, items[0].Content![0].Level);
        Assert.Equal(FormItemKind.Action, items[1].Kind);
        Assert.Equal(FormActionKinds.ScrollToSection, items[1].Action!.Kind);
        Assert.Equal("sec-b", items[1].Action!.SectionId);
        var zone = items[3];
        Assert.Equal(FormItemKind.Group, zone.Kind);
        Assert.Equal("md", zone.Layout!.CollapseBelow);
        Assert.Equal("compact", zone.Layout.Density);
        Assert.Equal("1/3", zone.Placement!["photoFront"].Width);
    }

    [Fact]
    public void Definition_with_blocks_and_zones_round_trips_through_System_Text_Json_web_defaults()
    {
        var def = LayoutBreadthForm();

        var node = JsonSerializer.SerializeToNode(def, Web);
        var rt = node.Deserialize<FormDefinition>(Web)!;

        var items = rt.Overlay.Sections[0].Items!;
        Assert.Equal(FormItemKind.Content, items[0].Kind);
        Assert.Equal("Before you start", items[0].Content![0].Text.Values["en"]);
        Assert.Equal(FormItemKind.Action, items[1].Kind);
        Assert.Equal(FormActionKinds.OpenUrl, items[2].Action!.Kind);
        Assert.Equal("https://example.test/guide", items[2].Action!.Url);
        Assert.Equal("start", rt.Overlay.Sections[0].Layout!.Align);
        Assert.Equal("sm", rt.Overlay.Sections[0].Layout!.CollapseBelow);
    }

    [Fact]
    public async Task Blockless_zoneless_definition_is_unchanged_back_compat()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var legacy = LegacyForm();

        await store.RegisterAsync(legacy);
        var loaded = await store.GetAsync(Tenant, legacy.Id, legacy.Version);

        var group = loaded.Overlay.Sections[0].Items!.Single(i => i.Kind == FormItemKind.Group);
        Assert.Null(group.Content);
        Assert.Null(group.Action);
        Assert.Null(group.Layout);
        Assert.Null(group.Placement);
        Assert.Null(loaded.Overlay.Sections[0].Layout!.CollapseBelow);
        Assert.Null(loaded.Overlay.Sections[0].Layout!.Density);
        Assert.Null(loaded.Overlay.Sections[0].Layout!.Align);
    }

    // ── fail-closed content-block invariants ──────────────────────────────────

    [Fact]
    public void Content_block_without_nodes_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(new FormItem(FormItemKind.Content, "instructions")),
            FormDefinitionCodes.BlocksContentMissingNodes);

    [Fact]
    public void Content_node_with_unknown_kind_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfContent("instructions", new[]
            {
                new ContentNode("html", Text("<b>bold</b>")),
            })),
            FormDefinitionCodes.BlocksContentUnknownNodeKind);

    [Fact]
    public void Content_node_with_empty_text_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfContent("instructions", new[]
            {
                new ContentNode(ContentNodeKinds.Paragraph, Text("   ")),
            })),
            FormDefinitionCodes.BlocksContentEmptyText);

    [Fact]
    public void Heading_level_out_of_range_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfContent("instructions", new[]
            {
                new ContentNode(ContentNodeKinds.Heading, Text("Hi"), Level: 9),
            })),
            FormDefinitionCodes.BlocksContentBadHeadingLevel);

    [Fact]
    public void Content_block_over_the_node_cap_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfContent(
                "instructions",
                Enumerable.Range(0, FormDefinitionValidation.MaxContentNodes + 1)
                    .Select(i => new ContentNode(ContentNodeKinds.Paragraph, Text($"p{i}")))
                    .ToList())),
            FormDefinitionCodes.BlocksContentTooManyNodes);

    [Fact]
    public void Content_block_with_children_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(new FormItem(
                FormItemKind.Content,
                "instructions",
                Items: new[] { FormItem.OfField("name") },
                Content: new[] { new ContentNode(ContentNodeKinds.Paragraph, Text("Hi")) })),
            FormDefinitionCodes.BlocksBlockHasChildren);

    // ── fail-closed action-block invariants ───────────────────────────────────

    [Fact]
    public void Action_block_without_config_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(new FormItem(FormItemKind.Action, "jump")),
            FormDefinitionCodes.BlocksMissingPayload);

    [Fact]
    public void Action_with_unknown_kind_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfAction("jump", new FormActionConfig("run-script", Text("Go")))),
            FormDefinitionCodes.BlocksActionUnknownKind);

    [Fact]
    public void Action_with_empty_label_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(FormItem.OfAction("jump", new FormActionConfig(
                FormActionKinds.ScrollToSection, Text(" "), SectionId: "sec-a"))),
            FormDefinitionCodes.BlocksActionEmptyLabel);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PGI+")]
    [InlineData("ftp://example.test/file")]
    public void OpenUrl_action_without_an_absolute_http_url_is_rejected(string? url)
        => AssertRejected(
            Items(FormItem.OfAction("guide", new FormActionConfig(
                FormActionKinds.OpenUrl, Text("Guidelines"), Url: url))),
            FormDefinitionCodes.BlocksActionBadUrl);

    [Fact]
    public void ScrollToSection_action_targeting_an_undeclared_section_is_rejected()
        => AssertRejected(
            Items(FormItem.OfAction("jump", new FormActionConfig(
                FormActionKinds.ScrollToSection, Text("Jump"), SectionId: "sec-ghost"))),
            FormDefinitionCodes.BlocksActionUnknownSection);

    [Fact]
    public async Task ScrollToSection_action_may_target_a_LATER_section()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        // The action lives in sec-a and targets sec-b (declared after it) — valid.
        var def = Items(FormItem.OfAction("jump", new FormActionConfig(
            FormActionKinds.ScrollToSection, Text("Jump to details"), SectionId: "sec-b")));
        await store.RegisterAsync(def);
    }

    // ── fail-closed zone / layout-intent invariants ────────────────────────────

    [Fact]
    public void Zone_layout_on_a_collection_is_rejected_with_a_stable_code()
        => AssertRejected(
            Items(new FormItem(
                FormItemKind.Collection,
                "rows",
                Items: new[] { FormItem.OfField("name") },
                Layout: Grid())),
            FormDefinitionCodes.LayoutZoneNotGroup);

    [Fact]
    public void Unknown_breakpoint_token_on_a_group_zone_is_rejected()
        => AssertRejected(
            Items(new FormItem(
                FormItemKind.Group,
                "zone",
                Items: new[] { FormItem.OfField("name") },
                Layout: Grid() with { CollapseBelow = "xl" })),
            FormDefinitionCodes.LayoutUnknownBreakpoint);

    [Fact]
    public void Unknown_density_token_on_a_section_layout_is_rejected()
        => AssertRejected(
            SectionLayoutForm(Grid() with { Density = "cozy" }),
            FormDefinitionCodes.LayoutUnknownDensity);

    [Fact]
    public void Unknown_align_token_on_a_section_layout_is_rejected()
        => AssertRejected(
            SectionLayoutForm(Grid() with { Align = "middle" }),
            FormDefinitionCodes.LayoutUnknownAlign);

    [Fact]
    public void Unknown_width_token_in_a_section_placement_is_rejected()
        => AssertRejected(
            SectionLayoutForm(Grid(), placement: new Dictionary<string, FieldPlacement>
            {
                ["name"] = new(Width: "37px"),
            }),
            FormDefinitionCodes.LayoutUnknownWidth);

    [Fact]
    public void Unknown_align_token_in_a_group_zone_placement_is_rejected()
        => AssertRejected(
            Items(new FormItem(
                FormItemKind.Group,
                "zone",
                Items: new[] { FormItem.OfField("name") },
                Layout: Grid(),
                Placement: new Dictionary<string, FieldPlacement> { ["name"] = new(Align: "top") })),
            FormDefinitionCodes.LayoutUnknownAlign);

    // ── reusable-unit posture (F-23 × D4) ─────────────────────────────────────

    [Fact]
    public void ScrollToSection_inside_a_reusable_unit_body_is_rejected_fail_closed()
    {
        // A unit body declares no sections, so a scroll target cannot resolve at unit
        // registration — fail-closed rather than silently admitting a dangling target.
        var unit = new ReusableUnit(
            Id: new ReusableUnitId("tenant:acme/with-action"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Kind: ReusableUnitKind.FormComponent,
            Owner: IdentityRef.System,
            Component: new FormComponentBody(
                Items: new[]
                {
                    FormItem.OfField("note"),
                    FormItem.OfAction("jump", new FormActionConfig(
                        FormActionKinds.ScrollToSection, Text("Jump"), SectionId: "anything")),
                },
                Fields: new Dictionary<string, FieldOverlay>
                {
                    ["note"] = new(Text("Note")),
                }),
            Title: Text("With action"),
            CreatedAt: Now,
            UpdatedAt: Now);

        var ex = Assert.Throws<ReusableUnitValidationException>(
            () => ReusableUnitValidation.ValidateOrThrow(unit));
        Assert.Equal(FormDefinitionCodes.BlocksActionUnknownSection, ex.Code);
    }

    // ── helpers / fixtures ─────────────────────────────────────────────────────

    private static InternationalizedText Text(string en)
        => new("en", new Dictionary<string, string> { ["en"] = en });

    private static SectionLayout Grid() => new(SectionLayoutKind.Grid, Columns: 2);

    private static void AssertRejected(FormDefinition def, string expectedCode)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var ex = Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(def))
            .GetAwaiter()
            .GetResult();
        Assert.Equal(expectedCode, ex.Code);
    }

    /// <summary>A two-section form whose FIRST section carries <paramref name="items"/>
    /// (plus the declared field leaf) as its item tree.</summary>
    private static FormDefinition Items(params FormItem[] items)
        => NewForm(
            sectionAItems: items.Concat(new[] { FormItem.OfField("name") }).ToList(),
            sectionALayout: null,
            sectionAPlacement: null);

    /// <summary>A two-section form whose FIRST section carries a flat field list plus the
    /// given section-grain layout/placement.</summary>
    private static FormDefinition SectionLayoutForm(
        SectionLayout layout,
        IReadOnlyDictionary<string, FieldPlacement>? placement = null)
        => NewForm(sectionAItems: null, sectionALayout: layout, sectionAPlacement: placement);

    /// <summary>The full F-23 showcase: content + two actions + a photo-strip zone in one
    /// section (with responsive section intents), a plain second section.</summary>
    private static FormDefinition LayoutBreadthForm()
        => NewForm(
            sectionAItems: new[]
            {
                FormItem.OfContent("instructions", new[]
                {
                    new ContentNode(ContentNodeKinds.Heading, Text("Before you start"), Level: 3),
                    new ContentNode(ContentNodeKinds.Paragraph, Text("Inspect every room top to bottom.")),
                }),
                FormItem.OfAction("jump", new FormActionConfig(
                    FormActionKinds.ScrollToSection, Text("Skip to details"), SectionId: "sec-b")),
                FormItem.OfAction("guide", new FormActionConfig(
                    FormActionKinds.OpenUrl, Text("Open the guide"), Url: "https://example.test/guide")),
                new FormItem(
                    FormItemKind.Group,
                    "photoStrip",
                    Items: new[] { FormItem.OfField("photoFront"), FormItem.OfField("photoBack") },
                    Title: Text("Photos"),
                    Layout: new SectionLayout(
                        SectionLayoutKind.Flex,
                        Gap: 2,
                        CollapseBelow: "md",
                        Density: "compact"),
                    Placement: new Dictionary<string, FieldPlacement>
                    {
                        ["photoFront"] = new(Width: "1/3"),
                        ["photoBack"] = new(Width: "1/3", Align: "end"),
                    }),
                FormItem.OfField("name"),
            },
            sectionALayout: new SectionLayout(
                SectionLayoutKind.Grid, Columns: 2, CollapseBelow: "sm", Align: "start"),
            sectionAPlacement: null);

    /// <summary>A pre-F-23 shape: a flat grid layout + a plain group tree, no new members.</summary>
    private static FormDefinition LegacyForm()
        => NewForm(
            sectionAItems: new[]
            {
                FormItem.OfField("name"),
                FormItem.OfGroup("pair", new[]
                {
                    FormItem.OfField("photoFront"),
                    FormItem.OfField("photoBack"),
                }),
            },
            sectionALayout: new SectionLayout(SectionLayoutKind.Grid, Columns: 2),
            sectionAPlacement: null);

    private static FormDefinition NewForm(
        IReadOnlyList<FormItem>? sectionAItems,
        SectionLayout? sectionALayout,
        IReadOnlyDictionary<string, FieldPlacement>? sectionAPlacement)
    {
        var fields = new[] { "name", "photoFront", "photoBack", "details" };
        var sections = new[]
        {
            new FormSection(
                "sec-a",
                Text("sec-a"),
                Fields: new[] { "name" },
                Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") }),
                Layout: sectionALayout,
                FieldPlacement: sectionAPlacement,
                Items: sectionAItems),
            new FormSection(
                "sec-b",
                Text("sec-b"),
                Fields: new[] { "details" },
                Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") })),
        };
        return new FormDefinition(
            Id: new FormDefinitionId("layout-breadth-test"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:test-layout-breadth"),
            Overlay: new HarborlineOverlay(
                Fields: fields.ToDictionary(f => f, f => new FieldOverlay(Text(f))),
                Sections: sections,
                Rules: Array.Empty<RuleDefinition>()),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
    }

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
