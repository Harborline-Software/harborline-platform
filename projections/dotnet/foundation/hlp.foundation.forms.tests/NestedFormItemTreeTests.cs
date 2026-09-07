using System.Text.Json;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// ADR 0055 Rev 7 — nested sub-form items. Tests the recursive <see cref="FormItem"/>
/// tree: it round-trips through the durable store (System.Text.Json, the exact
/// EntityStore serializer options), a flat definition is unchanged (back-compat), and
/// the fail-closed authoring bounds (INV-S2 extension) REJECT an over-limit tree with a
/// stable localizable code.
/// </summary>
public sealed class NestedFormItemTreeTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The EntityStore's durable serializer options (durable round-trip parity).</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ── round-trip + structural intactness ────────────────────────────────────

    [Fact]
    public async Task Nested_tree_round_trips_through_the_store_intact()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var def = QuoteFormWithNestedTree();

        await store.RegisterAsync(def); // exercises the DEFAULT-limit tree validation
        var loaded = await store.GetAsync(Tenant, def.Id, def.Version);

        AssertQuoteTreeIntact(loaded.Overlay.Sections[0].Items);
    }

    [Fact]
    public void Nested_tree_round_trips_through_System_Text_Json_web_defaults_intact()
    {
        // The EntityStoreFormDefinitionStore serializes the whole FormDefinition with
        // these exact options; this proves the tree survives the DURABLE path.
        var def = QuoteFormWithNestedTree();

        var node = JsonSerializer.SerializeToNode(def, Web);
        var rt = node.Deserialize<FormDefinition>(Web)!;

        AssertQuoteTreeIntact(rt.Overlay.Sections[0].Items);
        // The ≥2-level proof: a field leaf lives two levels below the section.
        var collection = rt.Overlay.Sections[0].Items![2];
        Assert.Equal(FormItemKind.Collection, collection.Kind);
        Assert.Equal("amount", collection.Items![1].Key);
        Assert.Equal(FormItemKind.Field, collection.Items![1].Kind);
    }

    [Fact]
    public async Task Flat_definition_is_unchanged_back_compat()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var flat = FlatForm();

        // A flat form carries no tree — a depth-1 tree by definition.
        Assert.Null(flat.Overlay.Sections[0].Items);

        await store.RegisterAsync(flat);
        var loaded = await store.GetAsync(Tenant, flat.Id, flat.Version);

        Assert.Null(loaded.Overlay.Sections[0].Items);
        Assert.Equal(new[] { "amount" }, loaded.Overlay.Sections[0].Fields);

        // Durable round-trip keeps Items null (semantically unchanged from pre-Rev-7).
        var rt = JsonSerializer.SerializeToNode(flat, Web).Deserialize<FormDefinition>(Web)!;
        Assert.Null(rt.Overlay.Sections[0].Items);
    }

    // ── fail-closed authoring bounds (INV-S2 extension) ────────────────────────

    [Fact]
    public void Over_depth_tree_is_rejected_with_a_stable_code()
    {
        var tight = FormTreeLimits.Default with { MaxDepth = 2 };
        // depth 3: section → group(g1) → group(g2) → field(amount)
        var deep = FormItem.OfGroup("g1", new[]
        {
            FormItem.OfGroup("g2", new[] { FormItem.OfField("amount") }),
        });
        var def = FormWithItems(new[] { deep });

        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def, tight));
        Assert.Equal(FormDefinitionCodes.TreeDepthExceeded, ex.Code);
    }

    [Fact]
    public void Over_fanout_tree_is_rejected_with_a_stable_code()
    {
        var tight = FormTreeLimits.Default with { MaxNodes = 3 };
        // 4 nodes: group(g) + 3 field children — exceeds the 3-node cap.
        var wide = FormItem.OfGroup("g", new[]
        {
            FormItem.OfField("a"), FormItem.OfField("b"), FormItem.OfField("c"),
        });
        var def = FormWithItems(new[] { wide }, extraFields: new[] { "a", "b", "c" });

        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def, tight));
        Assert.Equal(FormDefinitionCodes.TreeTooManyNodes, ex.Code);
    }

    [Fact]
    public void Field_item_referencing_an_undeclared_field_is_rejected()
    {
        var def = FormWithItems(new[] { FormItem.OfField("ghost") }); // not in Overlay.Fields
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeUnknownField, ex.Code);
    }

    [Fact]
    public void Empty_container_is_rejected()
    {
        var def = FormWithItems(new[] { new FormItem(FormItemKind.Group, "g", Array.Empty<FormItem>()) });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeEmptyContainer, ex.Code);
    }

    [Fact]
    public void Field_item_with_children_is_rejected()
    {
        var def = FormWithItems(new[]
        {
            new FormItem(FormItemKind.Field, "amount", new[] { FormItem.OfField("amount") }),
        });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeFieldHasChildren, ex.Code);
    }

    [Fact]
    public void Invalid_collection_cardinality_is_rejected()
    {
        var badCard = FormItem.OfCollection(
            "rows",
            new[] { FormItem.OfField("amount") },
            new Cardinality(Min: 5, Max: 2)); // max < min
        var def = FormWithItems(new[] { badCard });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeBadCardinality, ex.Code);
    }

    [Fact]
    public void Duplicate_sibling_key_is_rejected()
    {
        var def = FormWithItems(new[] { FormItem.OfField("amount"), FormItem.OfField("amount") });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeDuplicateKey, ex.Code);
    }

    [Fact]
    public void Empty_key_is_rejected()
    {
        var def = FormWithItems(new[] { FormItem.OfField(string.Empty) });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeEmptyKey, ex.Code);
    }

    [Fact]
    public void A_valid_nested_tree_passes_the_default_bounds()
    {
        var def = QuoteFormWithNestedTree();
        // Does not throw under the default limits.
        FormDefinitionValidation.ValidateOverlayOrThrow(def);
    }

    // ── F-24 (item 5) — collection tabular presentation admission ────────────────

    [Fact]
    public void Table_on_a_non_collection_is_rejected()
    {
        var group = FormItem.OfGroup("g", new[] { FormItem.OfField("amount") })
            with { Table = new CollectionTableConfig(Totals: new[] { "amount" }) };
        var def = FormWithItems(new[] { group });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.CollectionTableNotCollection, ex.Code);
    }

    [Fact]
    public void Table_total_referencing_a_non_row_field_is_rejected()
    {
        var coll = FormItem.OfCollection("rows", new[] { FormItem.OfField("amount") })
            with { Table = new CollectionTableConfig(Totals: new[] { "not-a-column" }) };
        var def = FormWithItems(new[] { coll });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.CollectionTotalUnknownField, ex.Code);
    }

    [Fact]
    public void Table_column_referencing_a_non_row_field_is_rejected()
    {
        var coll = FormItem.OfCollection("rows", new[] { FormItem.OfField("amount") })
            with { Table = new CollectionTableConfig(
                Columns: new Dictionary<string, CollectionColumn> { ["ghost"] = new(Width: "1/2") }) };
        var def = FormWithItems(new[] { coll });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.CollectionColumnUnknownField, ex.Code);
    }

    [Fact]
    public void Table_column_with_an_unknown_width_token_is_rejected()
    {
        var coll = FormItem.OfCollection("rows", new[] { FormItem.OfField("amount") })
            with { Table = new CollectionTableConfig(
                Columns: new Dictionary<string, CollectionColumn> { ["amount"] = new(Width: "wide") }) };
        var def = FormWithItems(new[] { coll });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.LayoutUnknownWidth, ex.Code);
    }

    [Fact]
    public void A_valid_collection_table_passes()
    {
        var coll = FormItem.OfCollection("rows", new[] { FormItem.OfField("amount"), FormItem.OfField("description") })
            with { Table = new CollectionTableConfig(
                Columns: new Dictionary<string, CollectionColumn> { ["amount"] = new(Width: "1/4", Align: "end") },
                Totals: new[] { "amount" }) };
        var def = FormWithItems(new[] { coll });
        FormDefinitionValidation.ValidateOverlayOrThrow(def); // does not throw.
    }

    // ── item 4 — GLOBAL cross-section top-level key uniqueness ───────────────────

    [Fact]
    public void Cross_section_top_level_key_collision_is_rejected()
    {
        var access = new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") });
        var s1 = new FormSection("s1", InternationalizedText.FromInvariant("S1"), Fields: new[] { "dup" }, Access: access);
        var s2 = new FormSection("s2", InternationalizedText.FromInvariant("S2"), Fields: new[] { "dup" }, Access: access);
        var def = new FormDefinition(
            Id: new FormDefinitionId("collide"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:test-collide"),
            Overlay: new HarborlineOverlay(
                Fields: new Dictionary<string, FieldOverlay> { ["dup"] = new(InternationalizedText.FromInvariant("dup")) },
                Sections: new[] { s1, s2 },
                Rules: Array.Empty<RuleDefinition>()),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.TreeGlobalDuplicateKey, ex.Code);
    }

    // ── #1679 review folds — block admission tightening (F3 / F4) ────────────────

    [Fact]
    public void Content_heading_below_level_2_is_rejected()
    {
        var content = FormItem.OfContent("intro", new[]
        {
            new ContentNode(ContentNodeKinds.Heading, InternationalizedText.FromInvariant("Title"), Level: 1),
        });
        var def = FormWithItems(new[] { content });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.BlocksContentBadHeadingLevel, ex.Code);
    }

    [Fact]
    public void Open_url_action_carrying_a_section_target_is_rejected()
    {
        var action = FormItem.OfAction("btn", new FormActionConfig(
            FormActionKinds.OpenUrl, InternationalizedText.FromInvariant("Go"),
            Url: "https://example.test", SectionId: "quote"));
        var def = FormWithItems(new[] { action });
        var ex = Assert.Throws<FormDefinitionValidationException>(
            () => FormDefinitionValidation.ValidateOverlayOrThrow(def));
        Assert.Equal(FormDefinitionCodes.BlocksActionExtraneousTarget, ex.Code);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void AssertQuoteTreeIntact(IReadOnlyList<FormItem>? items)
    {
        Assert.NotNull(items);
        Assert.Equal(3, items!.Count);

        Assert.Equal(FormItemKind.Field, items[0].Kind);
        Assert.Equal("total", items[0].Key);

        Assert.Equal(FormItemKind.Group, items[1].Kind);
        Assert.Equal("billTo", items[1].Key);
        Assert.Equal(new[] { "billName", "billEmail" }, items[1].Items!.Select(i => i.Key));

        Assert.Equal(FormItemKind.Collection, items[2].Kind);
        Assert.Equal("lineItems", items[2].Key);
        Assert.Equal(50, items[2].Cardinality!.Max);
        Assert.Equal(1, items[2].Cardinality!.Min);
        Assert.Equal(new[] { "description", "amount" }, items[2].Items!.Select(i => i.Key));
    }

    /// <summary>The reference nested "quote" form: a top-level field, a cardinality-1
    /// group, and a cardinality-N collection of fields (≥2 levels deep).</summary>
    private static FormDefinition QuoteFormWithNestedTree()
    {
        var items = new[]
        {
            FormItem.OfField("total"),
            FormItem.OfGroup("billTo", new[]
            {
                FormItem.OfField("billName"),
                FormItem.OfField("billEmail"),
            }, InternationalizedText.FromInvariant("Bill to")),
            FormItem.OfCollection("lineItems", new[]
            {
                FormItem.OfField("description"),
                FormItem.OfField("amount"),
            }, new Cardinality(Min: 1, Max: 50), InternationalizedText.FromInvariant("Line items")),
        };
        return NewForm(
            fields: new[] { "total", "billName", "billEmail", "description", "amount" },
            section: Section(flatFields: new[] { "total" }, items: items));
    }

    private static FormDefinition FlatForm()
        => NewForm(fields: new[] { "amount" }, section: Section(flatFields: new[] { "amount" }, items: null));

    private static FormDefinition FormWithItems(IReadOnlyList<FormItem> items, string[]? extraFields = null)
    {
        var fields = new List<string> { "total", "amount", "billName", "billEmail", "description", "rows" };
        if (extraFields is not null) fields.AddRange(extraFields);
        return NewForm(fields.ToArray(), Section(flatFields: new[] { "amount" }, items: items));
    }

    private static FormSection Section(IReadOnlyList<string> flatFields, IReadOnlyList<FormItem>? items)
        => new(
            "quote",
            InternationalizedText.FromInvariant("Quote"),
            Fields: flatFields,
            Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") }),
            Items: items);

    private static FormDefinition NewForm(IReadOnlyList<string> fields, FormSection section)
        => new(
            Id: new FormDefinitionId("quote"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:test-quote"),
            Overlay: new HarborlineOverlay(
                Fields: fields.ToDictionary(f => f, f => new FieldOverlay(InternationalizedText.FromInvariant(f))),
                Sections: new[] { section },
                Rules: Array.Empty<RuleDefinition>()),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
