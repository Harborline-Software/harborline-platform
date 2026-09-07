using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// D4 reuse-unit primitive (ADR 0135 amendment 2026-07-01) — the immutable-versioned,
/// tenant-scoped <see cref="IReusableUnitStore"/> + its fail-closed body validation. Mirrors
/// the <see cref="IFormDefinitionStore"/> keystone contract: immutable revisions, lifecycle
/// transitions, tenant isolation, latest-published selection.
/// </summary>
public sealed class ReusableUnitStoreTests
{
    private static readonly TenantId Tenant = new("tenant:acme");
    private static readonly TenantId Other = new("tenant:globex");
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    // ── immutable versioning ──────────────────────────────────────────────────────

    [Fact]
    public async Task Register_then_get_round_trips_the_unit()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        var unit = AddressBlock(new SemanticVersion(1, 0, 0));

        await store.RegisterAsync(unit);
        var loaded = await store.GetAsync(Tenant, unit.Id, unit.Version);

        Assert.Equal(unit.Id, loaded.Id);
        Assert.Equal(ReusableUnitKind.FormComponent, loaded.Kind);
        Assert.Equal(new[] { "city", "postcode", "street" }, loaded.Component!.Fields.Keys.Order());
    }

    [Fact]
    public async Task Re_registering_the_same_version_conflicts_immutable()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        var v1 = AddressBlock(new SemanticVersion(1, 0, 0));
        await store.RegisterAsync(v1);

        await Assert.ThrowsAsync<ReusableUnitConflictException>(() => store.RegisterAsync(v1).AsTask());
    }

    [Fact]
    public async Task A_new_version_is_a_distinct_immutable_record()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(1, 0, 0)));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(1, 1, 0)));

        var v1 = await store.GetAsync(Tenant, "addr", new SemanticVersion(1, 0, 0));
        var v11 = await store.GetAsync(Tenant, "addr", new SemanticVersion(1, 1, 0));
        Assert.Equal(new SemanticVersion(1, 0, 0), v1.Version);
        Assert.Equal(new SemanticVersion(1, 1, 0), v11.Version);
    }

    // ── lifecycle + latest-published selection ─────────────────────────────────────

    [Fact]
    public async Task GetCurrentPublished_returns_the_highest_published_version()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(1, 0, 0)));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(2, 0, 0)));
        await store.PublishAsync(Tenant, "addr", new SemanticVersion(1, 0, 0));
        await store.PublishAsync(Tenant, "addr", new SemanticVersion(2, 0, 0));

        var current = await store.GetCurrentPublishedAsync(Tenant, "addr");
        Assert.Equal(new SemanticVersion(2, 0, 0), current!.Version);
    }

    [Fact]
    public async Task GetCurrentPublished_ignores_draft_only_units()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(1, 0, 0))); // Draft, never published

        Assert.Null(await store.GetCurrentPublishedAsync(Tenant, "addr"));
    }

    // ── tenant isolation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Units_do_not_leak_across_tenants()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        await store.RegisterAsync(AddressBlock(new SemanticVersion(1, 0, 0)));

        await Assert.ThrowsAsync<ReusableUnitNotFoundException>(
            () => store.GetAsync(Other, "addr", new SemanticVersion(1, 0, 0)).AsTask());
    }

    // ── fail-closed body validation ─────────────────────────────────────────────────

    [Fact]
    public async Task FormComponent_without_a_body_is_rejected()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        var bad = AddressBlock(new SemanticVersion(1, 0, 0)) with { Component = null };

        var ex = await Assert.ThrowsAsync<ReusableUnitValidationException>(() => store.RegisterAsync(bad).AsTask());
        Assert.Equal(ReusableUnitCodes.WrongKindBody, ex.Code);
    }

    [Fact]
    public async Task WorkflowSubgraph_carrying_a_form_body_is_rejected()
    {
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        var bad = AddressBlock(new SemanticVersion(1, 0, 0)) with { Kind = ReusableUnitKind.WorkflowSubgraph };

        var ex = await Assert.ThrowsAsync<ReusableUnitValidationException>(() => store.RegisterAsync(bad).AsTask());
        Assert.Equal(ReusableUnitCodes.WrongKindBody, ex.Code);
    }

    [Fact]
    public async Task Workflow_subgraph_unit_stores_kind_agnostically_without_a_body()
    {
        // The envelope + store are kind-agnostic: a workflow unit (no form body) registers fine.
        // The FORM resolver is what fences it (covered in the resolver tests).
        using var store = new InMemoryReusableUnitStore(new FixedClock(Now));
        var wf = new ReusableUnit(
            Id: "wf-approval",
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: Tenant,
            Kind: ReusableUnitKind.WorkflowSubgraph,
            Owner: IdentityRef.System,
            Component: null,
            Title: InternationalizedText.FromInvariant("Approval subgraph"),
            CreatedAt: Now,
            UpdatedAt: Now);

        var stored = await store.RegisterAsync(wf);
        Assert.Equal(ReusableUnitKind.WorkflowSubgraph, stored.Kind);
    }

    [Fact]
    public void Empty_body_is_rejected()
    {
        var bad = AddressBlock(new SemanticVersion(1, 0, 0)) with
        {
            Component = new FormComponentBody(Array.Empty<FormItem>(), new Dictionary<string, FieldOverlay>()),
        };

        var ex = Assert.Throws<ReusableUnitValidationException>(() => ReusableUnitValidation.ValidateOrThrow(bad));
        Assert.Equal(ReusableUnitCodes.EmptyBody, ex.Code);
    }

    [Fact]
    public void Body_field_item_referencing_an_undeclared_field_is_rejected()
    {
        var bad = AddressBlock(new SemanticVersion(1, 0, 0)) with
        {
            Component = new FormComponentBody(
                new[] { FormItem.OfField("ghost") }, // not in the body's field map
                new Dictionary<string, FieldOverlay> { ["street"] = Overlay("Street") }),
        };

        var ex = Assert.Throws<ReusableUnitValidationException>(() => ReusableUnitValidation.ValidateOrThrow(bad));
        Assert.Equal(FormDefinitionCodes.TreeUnknownField, ex.Code);
    }

    [Fact]
    public void A_nested_reference_inside_a_unit_body_is_rejected_phase1()
    {
        // Unit-composing-unit is deferred: a Reference node inside a unit body is rejected.
        var bad = AddressBlock(new SemanticVersion(1, 0, 0)) with
        {
            Component = new FormComponentBody(
                new[] { FormItem.OfReference("inner", new ReusableUnitRef("other", ReusableUnitVersionSelector.LatestPublished)) },
                new Dictionary<string, FieldOverlay>()),
        };

        var ex = Assert.Throws<ReusableUnitValidationException>(() => ReusableUnitValidation.ValidateOrThrow(bad));
        Assert.Equal(FormDefinitionCodes.TreeReferenceNotAllowed, ex.Code);
    }

    [Fact]
    public void Over_depth_body_tree_is_rejected_with_a_stable_code()
    {
        var tight = FormTreeLimits.Default with { MaxDepth = 2 };
        var deep = FormItem.OfGroup("g1", new[]
        {
            FormItem.OfGroup("g2", new[] { FormItem.OfField("street") }),
        });
        var unit = AddressBlock(new SemanticVersion(1, 0, 0)) with
        {
            Component = new FormComponentBody(new[] { deep }, new Dictionary<string, FieldOverlay> { ["street"] = Overlay("Street") }),
        };

        var ex = Assert.Throws<ReusableUnitValidationException>(() => ReusableUnitValidation.ValidateOrThrow(unit, tight));
        Assert.Equal(FormDefinitionCodes.TreeDepthExceeded, ex.Code);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static ReusableUnit AddressBlock(SemanticVersion version) => new(
        Id: "addr",
        Version: version,
        Status: FormDefinitionStatus.Draft,
        Tenant: Tenant,
        Kind: ReusableUnitKind.FormComponent,
        Owner: IdentityRef.System,
        Component: new FormComponentBody(
            Items: new[]
            {
                FormItem.OfField("street"),
                FormItem.OfField("city"),
                FormItem.OfField("postcode"),
            },
            Fields: new Dictionary<string, FieldOverlay>
            {
                ["street"] = Overlay("Street"),
                ["city"] = Overlay("City"),
                ["postcode"] = Overlay("Postcode"),
            }),
        Title: InternationalizedText.FromInvariant("Address"),
        CreatedAt: Now,
        UpdatedAt: Now);

    private static FieldOverlay Overlay(string label) => new(InternationalizedText.FromInvariant(label));

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
