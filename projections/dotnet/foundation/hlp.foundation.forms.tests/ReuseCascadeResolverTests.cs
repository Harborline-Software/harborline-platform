using System.Text.Json;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// D4 Phase 1 — the CP-locked reuse cascade (ADR 0135/0140 amendments 2026-07-01). A
/// definition references a reusable unit by id + version; the unit's properties resolve
/// CP-locked (the consumer cannot override); reuse is by-reference (a new unit version
/// propagates to a latest-published referencer — not a frozen copy) while a pin stays put;
/// units are immutable-versioned; the per-unit AP-override endpoint is absent (fenced).
/// </summary>
public sealed class ReuseCascadeResolverTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly SemanticVersion V1 = new(1, 0, 0);
    private static readonly SemanticVersion V2 = new(2, 0, 0);

    // ── reference by id+version resolves CP-locked ─────────────────────────────────

    [Fact]
    public async Task A_definition_references_a_unit_and_its_subtree_resolves_in_place()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await RegisterPublished(units, AddressBlock(V1, cityLabel: "City"));
        var resolver = new ReuseResolver(units);

        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), ownFields: new[] { "title" });
        var resolved = await resolver.ResolveAsync(def);

        // The reference expanded into a Group nesting the unit's item subtree under the ref key.
        var items = resolved.Effective.Overlay.Sections[0].Items!;
        var group = Assert.Single(items, i => i.Kind == FormItemKind.Group && i.Key == "shipTo");
        Assert.Equal(new[] { "street", "city", "postcode" }, group.Items!.Select(i => i.Key));

        // The unit's fields merged into the effective overlay (own "title" still present).
        Assert.True(resolved.Effective.Overlay.Fields.ContainsKey("title"));
        Assert.Equal(new[] { "city", "postcode", "street", "title" },
            resolved.Effective.Overlay.Fields.Keys.Order());
    }

    [Fact]
    public async Task Reused_fields_carry_cp_locked_provenance_naming_the_unit()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await RegisterPublished(units, AddressBlock(V1, cityLabel: "City"));
        var resolver = new ReuseResolver(units);

        var resolved = await resolver.ResolveAsync(ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), new[] { "title" }));

        var prov = resolved.FieldProvenance["city"];
        Assert.Equal(new ReusableUnitId("addr"), prov.UnitId);
        Assert.Equal(V1, prov.UnitVersion);
        Assert.Equal("shipTo", prov.ReferenceKey);
        Assert.Equal(ReuseLock.CpLocked, prov.Lock);
        // Every reused field is CP-locked; the own field is NOT in the provenance map.
        Assert.All(resolved.FieldProvenance.Values, p => Assert.Equal(ReuseLock.CpLocked, p.Lock));
        Assert.False(resolved.FieldProvenance.ContainsKey("title"));
    }

    // ── CP-locked: the consumer cannot override ─────────────────────────────────────

    [Fact]
    public async Task Consumer_declaring_a_unit_owned_field_is_rejected_locked_override()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await RegisterPublished(units, AddressBlock(V1, cityLabel: "City"));
        var resolver = new ReuseResolver(units);

        // The consumer tries to own "city" — a field the unit CP-locks.
        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), ownFields: new[] { "title", "city" });

        var ex = await Assert.ThrowsAsync<ReuseResolutionException>(() => resolver.ResolveAsync(def).AsTask());
        Assert.Equal(ReusableUnitCodes.LockedFieldOverride, ex.Code);
    }

    [Fact]
    public void The_reference_type_exposes_no_per_property_override_channel_phase1()
    {
        // Structural fence: ReusableUnitRef carries ONLY (unit id, version selector). The
        // per-unit AP-override endpoint is deferred behind 3 kill-triggers — there is no
        // override/patch member for a consumer to weaken a locked property in Phase 1.
        var props = typeof(ReusableUnitRef).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "UnitId", "Version" }, props.Order());
    }

    // ── reuse-by-reference: propagation vs pin ──────────────────────────────────────

    [Fact]
    public async Task A_new_unit_version_propagates_to_a_latest_published_referencer()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await RegisterPublished(units, AddressBlock(V1, cityLabel: "City v1"));
        var resolver = new ReuseResolver(units);

        // Stored ONCE; the same stored definition is re-resolved after v2 publishes.
        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), new[] { "title" });

        var before = await resolver.ResolveAsync(def);
        Assert.Equal("City v1", Label(before.Effective, "city"));
        Assert.Equal(V1, before.FieldProvenance["city"].UnitVersion);

        // Publish a NEW unit version — the definition is NOT re-authored.
        await RegisterPublished(units, AddressBlock(V2, cityLabel: "City v2"));

        var after = await resolver.ResolveAsync(def);
        Assert.Equal("City v2", Label(after.Effective, "city")); // propagated — not a frozen copy
        Assert.Equal(V2, after.FieldProvenance["city"].UnitVersion);
    }

    [Fact]
    public async Task A_pinned_reference_does_not_drift_when_a_newer_version_publishes()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await RegisterPublished(units, AddressBlock(V1, cityLabel: "City v1"));
        await RegisterPublished(units, AddressBlock(V2, cityLabel: "City v2"));
        var resolver = new ReuseResolver(units);

        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.Pin(V1)), new[] { "title" });
        var resolved = await resolver.ResolveAsync(def);

        Assert.Equal("City v1", Label(resolved.Effective, "city")); // pinned — stays at v1
        Assert.Equal(V1, resolved.FieldProvenance["city"].UnitVersion);
    }

    [Fact]
    public void The_stored_definition_holds_only_the_reference_not_an_expanded_copy()
    {
        // Reuse-by-reference proof at the persistence layer: a definition with a Reference node
        // round-trips through the durable (Web-defaults) serializer carrying only the ref — the
        // unit's content is never embedded, so a later unit version can propagate.
        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.Pin(V1)), new[] { "title" });

        var rt = JsonSerializer.SerializeToNode(def, Web).Deserialize<FormDefinition>(Web)!;

        var reference = Assert.Single(rt.Overlay.Sections[0].Items!, i => i.Kind == FormItemKind.Reference);
        Assert.Null(reference.Items); // no embedded subtree
        Assert.Equal(new ReusableUnitId("addr"), reference.Reference!.UnitId);
        Assert.Equal(V1, reference.Reference!.Version.PinnedVersion);
    }

    // ── fail-closed fences ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_reference_to_a_missing_unit_is_rejected_unresolved()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        var resolver = new ReuseResolver(units);

        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), new[] { "title" });
        var ex = await Assert.ThrowsAsync<ReuseResolutionException>(() => resolver.ResolveAsync(def).AsTask());
        Assert.Equal(ReusableUnitCodes.UnresolvedReference, ex.Code);
    }

    [Fact]
    public async Task A_latest_published_reference_to_a_draft_only_unit_is_unresolved()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        await units.RegisterAsync(AddressBlock(V1, cityLabel: "City")); // registered but NOT published
        var resolver = new ReuseResolver(units);

        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), new[] { "title" });
        var ex = await Assert.ThrowsAsync<ReuseResolutionException>(() => resolver.ResolveAsync(def).AsTask());
        Assert.Equal(ReusableUnitCodes.UnresolvedReference, ex.Code);
    }

    [Fact]
    public async Task A_form_referencing_a_workflow_subgraph_unit_is_fenced_phase1()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        var wf = new ReusableUnit(
            Id: "wf-approval", Version: V1, Status: FormDefinitionStatus.Draft, Tenant: Tenant,
            Kind: ReusableUnitKind.WorkflowSubgraph, Owner: IdentityRef.System, Component: null,
            Title: InternationalizedText.FromInvariant("Approval"), CreatedAt: Now, UpdatedAt: Now);
        await units.RegisterAsync(wf);
        await units.PublishAsync(Tenant, "wf-approval", V1);
        var resolver = new ReuseResolver(units);

        var def = ConsumingDef(new ReusableUnitRef("wf-approval", ReusableUnitVersionSelector.LatestPublished), new[] { "title" });
        var ex = await Assert.ThrowsAsync<ReuseResolutionException>(() => resolver.ResolveAsync(def).AsTask());
        Assert.Equal(ReusableUnitCodes.WorkflowSubgraphUnsupported, ex.Code);
    }

    // ── composition with the definition keystone ────────────────────────────────────

    [Fact]
    public async Task A_definition_carrying_a_reference_node_registers_in_the_form_store()
    {
        // The reference node is a valid FormItem in a definition tree (no field-declared check;
        // the referenced unit is resolved separately) — it registers through the keystone store.
        using var forms = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var def = ConsumingDef(Ref(ReusableUnitVersionSelector.LatestPublished), new[] { "title" });

        var stored = await forms.RegisterAsync(def);
        Assert.Contains(stored.Overlay.Sections[0].Items!, i => i.Kind == FormItemKind.Reference);
    }

    [Fact]
    public async Task A_definition_with_no_references_resolves_to_itself()
    {
        using var units = new InMemoryReusableUnitStore(new FixedClock(Now));
        var resolver = new ReuseResolver(units);

        var flat = ConsumingDefNoReference(new[] { "title" });
        var resolved = await resolver.ResolveAsync(flat);

        Assert.Empty(resolved.FieldProvenance);
        Assert.Same(flat, resolved.Effective);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static ReusableUnitRef Ref(ReusableUnitVersionSelector selector) => new("addr", selector);

    private static string Label(FormDefinition def, string field)
        => def.Overlay.Fields[field].Label.Resolve(new[] { "en" });

    private static async Task RegisterPublished(InMemoryReusableUnitStore store, ReusableUnit unit)
    {
        await store.RegisterAsync(unit);
        await store.PublishAsync(unit.Tenant, unit.Id, unit.Version);
    }

    private static ReusableUnit AddressBlock(SemanticVersion version, string cityLabel) => new(
        Id: "addr",
        Version: version,
        Status: FormDefinitionStatus.Draft,
        Tenant: Tenant,
        Kind: ReusableUnitKind.FormComponent,
        Owner: IdentityRef.System,
        Component: new FormComponentBody(
            Items: new[] { FormItem.OfField("street"), FormItem.OfField("city"), FormItem.OfField("postcode") },
            Fields: new Dictionary<string, FieldOverlay>
            {
                ["street"] = new(InternationalizedText.FromInvariant("Street")),
                ["city"] = new(InternationalizedText.FromInvariant(cityLabel)),
                ["postcode"] = new(InternationalizedText.FromInvariant("Postcode")),
            }),
        Title: InternationalizedText.FromInvariant("Address"),
        CreatedAt: Now,
        UpdatedAt: Now);

    /// <summary>A consuming definition whose section tree carries the own fields as leaves plus
    /// one reference to the address unit under key "shipTo".</summary>
    private static FormDefinition ConsumingDef(ReusableUnitRef unitRef, IReadOnlyList<string> ownFields)
    {
        var items = ownFields.Select(FormItem.OfField).Append(FormItem.OfReference("shipTo", unitRef)).ToList();
        return NewDef(ownFields, items);
    }

    private static FormDefinition ConsumingDefNoReference(IReadOnlyList<string> ownFields)
        => NewDef(ownFields, ownFields.Select(FormItem.OfField).ToList());

    private static FormDefinition NewDef(IReadOnlyList<string> ownFields, IReadOnlyList<FormItem> items) => new(
        Id: new FormDefinitionId("order"),
        Version: V1,
        Status: FormDefinitionStatus.Draft,
        Tenant: Tenant,
        Owner: IdentityRef.System,
        SchemaRef: new SchemaId("sha256:test-order"),
        Overlay: new HarborlineOverlay(
            Fields: ownFields.ToDictionary(f => f, f => new FieldOverlay(InternationalizedText.FromInvariant(f))),
            Sections: new[]
            {
                new FormSection(
                    "main",
                    InternationalizedText.FromInvariant("Main"),
                    Fields: ownFields,
                    Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") }),
                    Items: items),
            },
            Rules: Array.Empty<RuleDefinition>()),
        Lineage: null,
        CreatedAt: Now,
        UpdatedAt: Now);

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
