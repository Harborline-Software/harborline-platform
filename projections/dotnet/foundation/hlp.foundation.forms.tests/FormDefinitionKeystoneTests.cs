using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.DependencyInjection;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// Keystone-PR behaviour tests for the canonical <see cref="FormDefinition"/>
/// record + the <see cref="IFormDefinitionStore"/> facade + the in-memory
/// reference implementation. These tests exercise the contract surface
/// that every downstream consumer (form engine, entity store, rule
/// evaluator, authoring UX) will bind against.
/// </summary>
public sealed class FormDefinitionKeystoneTests
{
    private static readonly TenantId TenantA = new("tenant:acme");
    private static readonly TenantId TenantB = new("tenant:zenith");
    private static readonly DateTimeOffset Now = new(2026, 5, 30, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Register_then_Get_round_trips_the_record()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var schema = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA);

        var registered = await registry.RegisterAsync(schema);
        var loaded = await registry.GetAsync(TenantA, schema.Id, schema.Version);

        Assert.Equal(schema, registered);
        Assert.Equal(schema, loaded);
    }

    [Fact]
    public async Task Get_other_tenant_throws_NotFound()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var schema = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA);
        await registry.RegisterAsync(schema);

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => registry.GetAsync(TenantB, schema.Id, schema.Version).AsTask());
    }

    [Fact]
    public async Task Duplicate_version_throws_Conflict()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var schema = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA);
        await registry.RegisterAsync(schema);

        var conflict = await Assert.ThrowsAsync<FormDefinitionConflictException>(
            () => registry.RegisterAsync(schema).AsTask());
        Assert.Equal(schema.Id, conflict.SchemaId);
        Assert.Equal(schema.Version, conflict.Version);
    }

    [Fact]
    public async Task GetCurrentPublishedAsync_picks_highest_Published_version()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var v1 = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA, status: FormDefinitionStatus.Published);
        var v2 = NewSchema("property", new SemanticVersion(2, 0, 0), TenantA, status: FormDefinitionStatus.Published);
        var v3draft = NewSchema("property", new SemanticVersion(3, 0, 0), TenantA, status: FormDefinitionStatus.Draft);

        await registry.RegisterAsync(v1);
        await registry.RegisterAsync(v2);
        await registry.RegisterAsync(v3draft);

        var current = await registry.GetCurrentPublishedAsync(TenantA, v1.Id);
        Assert.NotNull(current);
        Assert.Equal(new SemanticVersion(2, 0, 0), current!.Version);
    }

    [Fact]
    public async Task PublishAsync_transitions_Draft_to_Published_and_stamps_UpdatedAt()
    {
        var clock = new FixedClock(Now);
        using var registry = new InMemoryFormDefinitionStore(clock);
        var draft = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA, status: FormDefinitionStatus.Draft);
        await registry.RegisterAsync(draft);

        clock.Advance(TimeSpan.FromMinutes(7));
        var published = await registry.PublishAsync(TenantA, draft.Id, draft.Version);

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal(Now.AddMinutes(7), published.UpdatedAt);
        Assert.Equal(draft.CreatedAt, published.CreatedAt);
    }

    [Fact]
    public async Task WithdrawAsync_then_PublishAsync_is_rejected()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var schema = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA, status: FormDefinitionStatus.Published);
        await registry.RegisterAsync(schema);
        await registry.WithdrawAsync(TenantA, schema.Id, schema.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.PublishAsync(TenantA, schema.Id, schema.Version).AsTask());
    }

    [Fact]
    public async Task Withdrawn_System_projection_can_be_explicitly_restored()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var schema = NewSchema(
            "pack-form", new SemanticVersion(1, 0, 0), TenantA,
            status: FormDefinitionStatus.Published);
        await registry.RegisterAsync(schema);
        await registry.WithdrawAsync(TenantA, schema.Id, schema.Version);

        var restored = await registry.RestorePackProjectionAsync(TenantA, schema.Id, schema.Version);

        Assert.Equal(FormDefinitionStatus.Published, restored.Status);
    }

    [Fact]
    public async Task Overlay_with_orphan_section_field_is_rejected()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var bogus = NewSchema("property", new SemanticVersion(1, 0, 0), TenantA) with
        {
            Overlay = new HarborlineOverlay(
                Fields: new Dictionary<string, FieldOverlay> { ["street"] = SimpleFieldOverlay("Street") },
                Sections: new[]
                {
                    new FormSection(
                        "address",
                        InternationalizedText.FromInvariant("Address"),
                        Fields: new[] { "street", "city" }, // 'city' is orphan
                        Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") })),
                },
                Rules: Array.Empty<RuleDefinition>()),
        };

        var ex = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => registry.RegisterAsync(bogus).AsTask());
        Assert.Contains("city", ex.Message);
    }

    [Fact]
    public async Task Lineage_to_unregistered_parent_is_rejected()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var orphanChild = NewSchema("equipment", new SemanticVersion(1, 0, 0), TenantA) with
        {
            Lineage = new FormDefinitionLineage(new FormDefinitionId("property"), new SemanticVersion(1, 0, 0)),
        };

        await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => registry.RegisterAsync(orphanChild).AsTask());
    }

    [Fact]
    public async Task ListByTenantAsync_returns_only_that_tenants_schemas()
    {
        using var registry = new InMemoryFormDefinitionStore(new FixedClock(Now));
        await registry.RegisterAsync(NewSchema("property", new SemanticVersion(1, 0, 0), TenantA));
        await registry.RegisterAsync(NewSchema("equipment", new SemanticVersion(1, 0, 0), TenantA));
        await registry.RegisterAsync(NewSchema("property", new SemanticVersion(1, 0, 0), TenantB));

        var listA = new List<FormDefinition>();
        await foreach (var s in registry.ListByTenantAsync(TenantA))
        {
            listA.Add(s);
        }

        Assert.Equal(2, listA.Count);
        Assert.All(listA, s => Assert.Equal(TenantA, s.Tenant));
    }

    [Fact]
    public async Task DI_registration_resolves_a_singleton_registry()
    {
        var sp = new ServiceCollection()
            .AddInMemoryFormDefinitionStore()
            .BuildServiceProvider();

        var a = sp.GetRequiredService<IFormDefinitionStore>();
        var b = sp.GetRequiredService<IFormDefinitionStore>();
        Assert.Same(a, b);
    }

    [Fact]
    public void SemanticVersion_parses_and_orders_structurally()
    {
        var v1 = SemanticVersion.Parse("1.2.3");
        var v2 = SemanticVersion.Parse("1.10.0");
        Assert.True(v1 < v2);
        Assert.Equal("1.2.3", v1.ToString());
        Assert.Throws<FormatException>(() => SemanticVersion.Parse("1.2"));
        Assert.Throws<FormatException>(() => SemanticVersion.Parse("1.2.3-beta"));
        Assert.Throws<FormatException>(() => SemanticVersion.Parse("1.-1.0"));
    }

    [Fact]
    public void InternationalizedText_resolves_locale_chain_then_default_then_first_available()
    {
        var text = new InternationalizedText(
            DefaultLocale: "en",
            Values: new Dictionary<string, string>
            {
                ["en"] = "Property",
                ["es"] = "Propiedad",
            });

        Assert.Equal("Propiedad", text.Resolve(new[] { "es-MX", "es" }));
        Assert.Equal("Property", text.Resolve(new[] { "fr-CA" })); // fallback to default
        Assert.Equal("Property", text.Resolve(new[] { "en-US" })); // not strictly matched in v1; falls through to default — kept minimal on the keystone
    }

    // -------------------- helpers --------------------

    private static FormDefinition NewSchema(
        string id,
        SemanticVersion version,
        TenantId tenant,
        FormDefinitionStatus status = FormDefinitionStatus.Draft)
        => new(
            Id: new FormDefinitionId(id),
            Version: version,
            Status: status,
            Tenant: tenant,
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId($"sha256:test-{id}-{version}"),
            Overlay: new HarborlineOverlay(
                Fields: new Dictionary<string, FieldOverlay> { ["street"] = SimpleFieldOverlay("Street") },
                Sections: new[]
                {
                    new FormSection(
                        "address",
                        InternationalizedText.FromInvariant("Address"),
                        Fields: new[] { "street" },
                        Access: new SectionAccess(ReadRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("*") }, WriteRoles: new[] { Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin") })),
                },
                Rules: Array.Empty<RuleDefinition>()),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);

    private static FieldOverlay SimpleFieldOverlay(string label)
        => new(Label: InternationalizedText.FromInvariant(label));

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
