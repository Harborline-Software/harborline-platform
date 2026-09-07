using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class EntityViewsWaveBTests
{
    private const string Building = "entity:seed/building";
    private const string Heater = "entity:seed/heater";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T12:00:00.000Z");

    [Fact(DisplayName = "types: the effective catalog lists the seeded pack type with its traits")]
    public async Task TypesListIncludesSeed()
    {
        var row = Assert.Single(await Fixture().ForTenant("a").Types.GetEffectiveCatalogAsync());
        Assert.Equal("water-heater", row.Id);
        Assert.Contains("maintainable", row.Traits);
        Assert.Contains("movable", row.Traits);
    }

    [Fact(DisplayName = "entities: create → get by id → list")]
    public async Task EntitiesCreateGetList()
    {
        var store = Fixture().ForTenant("a").Entities;
        var created = await store.CreateEntityAsync(new("water-heater", "Unit 4B water heater", null));
        Assert.Equal(created, await store.GetEntityAsync(created.Id));
        Assert.Contains(await store.ListEntitiesAsync("water-heater"), row => row.Id == created.Id);
    }

    [Fact(DisplayName = "tree: a contains edge shows a child + breadcrumb as-of now")]
    public async Task TreeContainsEdge()
    {
        var store = Fixture().ForTenant("a").Entities;
        var child = await store.CreateEntityAsync(new("water-heater", "Child", null));
        await store.AddEdgeAsync(new(EdgeKind.Contains, Building, child.Id));
        Assert.Contains((await store.GetTreeAsync(Building, null))!.Children, row => row.Id == child.Id);
        Assert.Contains(Building, (await store.GetTreeAsync(child.Id, null))!.Path);
    }

    [Fact(DisplayName = "edges: an endpoint that does not exist is rejected (static error, no payload reflection)")]
    public async Task MissingEndpointIsStaticRejection()
    {
        // HTTP 400 belongs to the later loopback host; this proves its typed semantic half.
        var error = await Assert.ThrowsAsync<EntityViewsException>(async () =>
            await Fixture().ForTenant("a").Entities.AddEdgeAsync(new(EdgeKind.Contains, Building, "secret-payload")));
        Assert.Equal(EntityViewsCodes.UnknownEntity, error.Code);
        Assert.DoesNotContain("secret-payload", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "LIVE CAPTURE: submitting the condition form writes the entity's condition (one act, two artifacts)")]
    public async Task LiveCaptureWritesCondition()
    {
        var scope = Fixture().ForTenant("a");
        var result = await scope.ConditionCapture.CaptureFromSubmission(Heater, "condition-form", "/condition", 4, 5, Now, null, null);
        Assert.NotNull(result.Assessment);
        var assessment = Assert.Single((await scope.History.GetConditionHistoryAsync(Heater, null)).History);
        Assert.Equal((4, 5, "condition-form", "/condition"), (assessment.Grade, assessment.ScaleMax, assessment.SourceForm, assessment.SourceField));
    }

    [Fact(DisplayName = "F3: an out-of-range grade is audit-skipped AND surfaced in the response (never silent success)")]
    public async Task OutOfRangeGradeSurfacesSkip()
    {
        var scope = Fixture().ForTenant("a");
        var result = await scope.ConditionCapture.CaptureFromSubmission(Heater, "wide", "/condition", 8, 5, Now, null, null);
        Assert.Equal("skipped", result.Projection);
        Assert.Equal(new ConditionCaptureSkip("grade-out-of-range", "/condition"), Assert.Single(result.Skips!));
        Assert.Null(result.Assessment);
        Assert.Empty((await scope.History.GetConditionHistoryAsync(Heater, null)).History);
    }

    [Fact(DisplayName = "F3: an in-range submit still returns a bare 201 (no projection/skips fields — byte-identical)")]
    public async Task InRangeHasNoSkipFields()
    {
        // HTTP 201 belongs to the later loopback host; null fields preserve omission at that adapter.
        var scope = Fixture().ForTenant("a");
        var result = await scope.ConditionCapture.CaptureFromSubmission(Heater, "wide", "/condition", 4, 5, Now, null, null);
        Assert.Null(result.Projection);
        Assert.Null(result.Skips);
        Assert.Single((await scope.History.GetConditionHistoryAsync(Heater, null)).History);
    }

    [Fact(DisplayName = "#144 LIVE CAPTURE via VisitCase THROUGH the route: the case header resolves the target")]
    public async Task VisitCaseResolvesTarget()
    {
        // Header transport is host-owned; this invokes the injected VisitCase resolver semantic.
        var scope = Fixture().ForTenant("a");
        await scope.ConditionCapture.CaptureFromSubmission(Heater, "case-condition", "/condition", 3, 5, Now, null, null);
        var link = await scope.SubmissionLinking.LinkFromCase(Heater, "case-condition", "instance-1", Now, null);
        Assert.NotNull(link);
        Assert.Single((await scope.History.GetConditionHistoryAsync(Heater, null)).History);
        Assert.Single((await scope.History.GetSubmissionsAsync(Heater)).Submissions);
    }

    [Fact(DisplayName = "#144 submissions: a submission-field form filled into a record is also linked")]
    public async Task SubmissionFieldFormAlsoLinks()
    {
        var scope = Fixture().ForTenant("a");
        var link = await scope.SubmissionLinking.LinkFromCase(Heater, "submission-field-form", "instance-2", Now, null);
        Assert.Equal("submission-field-form", link?.FormId);
    }

    [Fact(DisplayName = "#144 submissions: a submit with NO case header leaves no record link")]
    public async Task NoCaseHeaderLeavesNoLink()
    {
        var scope = Fixture().ForTenant("a");
        Assert.Null(await scope.SubmissionLinking.LinkFromCase(null, "form", "instance", Now, null));
        Assert.Empty((await scope.History.GetSubmissionsAsync(Heater)).Submissions);
    }

    [Fact(DisplayName = "#144 submissions: an unknown entity id is an opaque 404")]
    public async Task UnknownSubmissionEntityIsOpaque()
    {
        // HTTP 404 belongs to the later loopback host; entity null is its semantic discriminator.
        var scope = Fixture().ForTenant("a");
        Assert.Null(await scope.Entities.GetEntityAsync("missing"));
        Assert.Null(await scope.SubmissionLinking.LinkFromCase("missing", "form", "instance", Now, null));
    }

    [Fact(DisplayName = "#144 submissions: a record's submitted forms are invisible to another tenant")]
    public async Task SubmissionsAreTenantScoped()
    {
        var scopes = Fixture();
        await scopes.ForTenant("a").SubmissionLinking.LinkFromCase(Heater, "form", "instance", Now, null);
        Assert.Single((await scopes.ForTenant("a").History.GetSubmissionsAsync(Heater)).Submissions);
        Assert.Empty((await scopes.ForTenant("b").History.GetSubmissionsAsync(Heater)).Submissions);
    }

    [Fact(DisplayName = "tenant: switching the active team hides another org's entity (opaque 404)")]
    public async Task TenantIsolationIsServerSide()
    {
        // HTTP 404 belongs to the later loopback host; cross-scope null is its semantic half.
        var scopes = Fixture();
        var created = await scopes.ForTenant("a").Entities.CreateEntityAsync(new("water-heater", "A only", null));
        Assert.Null(await scopes.ForTenant("b").Entities.GetEntityAsync(created.Id));
    }

    [Fact(DisplayName = "types: create a tenant greenfield type → get by id → appears in the catalog")]
    public async Task CreateGreenfieldGetAndList()
    {
        var types = Fixture().ForTenant("a").Types;
        await types.CreateTypeAsync(Body("shed", "Storage shed", ["container", "maintainable"], 4));
        Assert.Equal("Storage shed", (await types.GetTypeAsync("shed"))?.DisplayName);
        Assert.Contains(await types.GetEffectiveCatalogAsync(), row => row.Id == "shed");
    }

    [Fact(DisplayName = "REFUTE-PROOF: editing a SEEDED type creates a tenant override — the shared seed is never mutated")]
    public async Task SeedEditCreatesOverride()
    {
        var scopes = Fixture();
        var changed = await scopes.ForTenant("a").Types.UpdateTypeAsync("water-heater", Body(null, "Boiler", ["maintainable", "movable"], null));
        Assert.True(changed?.OverridesSeed);
        Assert.Equal("Tenant", changed?.Provenance);
        var pristine = await scopes.ForTenant("b").Types.GetTypeAsync("water-heater");
        Assert.Equal("Water Heater", pristine?.DisplayName);
        Assert.Equal("Pack", pristine?.Provenance);
        Assert.False(pristine?.HasTenantRow);
    }

    [Fact(DisplayName = "types: revert discards the override so the shared pack seed is effective again")]
    public async Task RevertRestoresPackSeed()
    {
        var types = Fixture().ForTenant("a").Types;
        await types.UpdateTypeAsync("water-heater", Body(null, "Boiler", ["maintainable"], null));
        var reverted = await types.RevertTypeAsync("water-heater");
        Assert.Equal("Water Heater", reverted?.DisplayName);
        Assert.False(reverted?.OverridesSeed);
        Assert.False(reverted?.HasTenantRow);
    }

    [Fact(DisplayName = "types: a tenant's created type is invisible to another tenant (opaque 404)")]
    public async Task CreatedTypeIsTenantScoped()
    {
        // HTTP 404 belongs to the later loopback host; cross-scope null is its semantic half.
        var scopes = Fixture();
        await scopes.ForTenant("a").Types.CreateTypeAsync(Body("widget", "Widget", ["maintainable"], null));
        Assert.Null(await scopes.ForTenant("b").Types.GetTypeAsync("widget"));
    }

    [Fact(DisplayName = "types: property-form + per-discipline inspection forms + capital planning round-trip")]
    public async Task BindingsAndCapitalPlanningRoundTrip()
    {
        var body = Body(null, "Water heater", ["maintainable", "movable"], 5) with
        {
            Disciplines = ["plumbing", "hvac"], PropertyForm = new("wh-props", "2.1.0"),
            InspectionForms = [new("plumbing", "wh-plumbing-insp", "1.0.0"), new("hvac", "wh-hvac-insp", "1.2.0")],
            ExpectedUsefulLifeYears = 12, TypicalReplacementCost = new(1400.50m, "AED"),
        };
        var detail = await Fixture().ForTenant("a").Types.UpdateTypeAsync("water-heater", body);
        Assert.Equal(new PropertyFormWire("wh-props", "2.1.0"), detail?.PropertyForm);
        Assert.Equal(2, detail?.InspectionForms.Count);
        Assert.Equal(new MoneyWire(1400.50m, "AED"), detail?.TypicalReplacementCost);
        Assert.Equal(12, detail?.ExpectedUsefulLifeYears);
    }

    [Fact(DisplayName = "types: a bad form version is rejected (static error)")]
    public async Task BadFormVersionRejected()
    {
        // HTTP 400 belongs to the later loopback host; this proves the typed static rejection.
        var body = Body(null, "Water heater", ["maintainable"], null) with { PropertyForm = new("wh-props", "not-a-version") };
        var error = await Assert.ThrowsAsync<EntityViewsException>(async () => await Fixture().ForTenant("a").Types.UpdateTypeAsync("water-heater", body));
        Assert.Equal(EntityViewsTypeCodes.InvalidFormVersion, error.Code);
        Assert.DoesNotContain("not-a-version", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "types: a trait-less create is rejected")]
    public async Task TraitlessCreateRejected()
    {
        // HTTP 400 belongs to the later loopback host; this proves the typed static rejection.
        var error = await Assert.ThrowsAsync<EntityViewsException>(async () =>
            await Fixture().ForTenant("a").Types.CreateTypeAsync(Body("none", "No traits", [], null)));
        Assert.Equal(EntityViewsTypeCodes.AtLeastOneTraitRequired, error.Code);
    }

    private static TypeUpsertBody Body(string? id, string name, IReadOnlyList<string> traits, int? scale) =>
        new(id, name, traits, null, null, null, null, scale, null, null);

    private static EntityViewsTenantScopes Fixture()
    {
        var seedType = new TypeDetailWire(
            "water-heater", "Water Heater", ["maintainable", "movable"], null, ["plumbing"],
            "Pack", false, true, false, new("water-heater.props", "1.0.0"),
            [new("plumbing", "plumbing.inspection", "1.0.0")], 5, 12, new(1400m, "USD"));
        var types = new InMemoryTypeCatalogStore([seedType]);
        var entities = new[]
        {
            new EntityDetail(Building, "building", "Building", null, "2026-01-01T00:00:00.000Z", null, null, [], null),
            new EntityDetail(Heater, "water-heater", "Heater", null, "2026-01-01T00:00:00.000Z", null, Building, [Building], new("water-heater.props", "1.0.0")),
        };
        return new EntityViewsTenantScopes(
            entities, null, null, null, null, ["building", "water-heater"], types,
            new FixedTimeProvider(Now), value => ValueTask.FromResult<string?>(value));
    }

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
