using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Harborline.Blocks.EntityViews;

const string Building = "entity:preview/building-1";
const string Bedroom = "entity:preview/bedroom-1";
const string Heater = "entity:preview/water-heater-1";
var instant = DateTimeOffset.Parse("2026-07-16T00:00:00.000Z");
var clock = new FixedTimeProvider(instant);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var corpusPath = Path.Combine(AppContext.BaseDirectory, "views-vertical-cases.json");
var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(corpusPath));
Check(corpus.RootElement.GetProperty("anchors").GetArrayLength() == 9, "corpus must contain nine anchors");
Check(corpus.RootElement.GetProperty("pairs").GetArrayLength() == 4, "corpus must contain four pairs");
Check(corpus.RootElement.GetProperty("routes").GetArrayLength() == 20, "corpus must contain twenty route rows");

var preview = new DevelopmentPreviewAdapter("Development", clock);
await ProveAnchors(preview);
Console.WriteLine($"VIEWS_PACKAGE_PASS:{JsonSerializer.Serialize(new { anchors = 9, routeRows = 20 })}");

// A FRESH fallback adapter: ProveAnchors mutated `preview` (anchor 7 creates + contains an
// entity), and the four-pair proof compares the two lanes at their identical SEED state.
await ProvePairs(new DevelopmentPreviewAdapter("Development", clock), DurableStandIn(clock));
ProveAssemblyClosure();
ProveProductionGuard(clock);
await ProveFailureAndBreadcrumbGuards(clock);
Console.WriteLine($"VIEWS_CAPABILITY_PASS:{JsonSerializer.Serialize(new { pairs = 4, failedConditionGuards = 4 })}");

async Task ProveAnchors(DevelopmentPreviewAdapter store)
{
    // The factory is exercised as the packaged ambient-tenant composition root.
    var scopes = MakeScopes(clock);
    Check(!ReferenceEquals(scopes.ForTenant("anchor-a").Entities, scopes.ForTenant("anchor-b").Entities), "tenant scopes must be isolated");

    var all = await store.ListEntitiesAsync(null);
    Check(all.Count == 4 && all.Select(x => x.DisplayName).SequenceEqual(all.Select(x => x.DisplayName).Order(StringComparer.Ordinal)), "anchor 1");
    Check((await store.ListEntitiesAsync("water-heater")).Select(x => x.Id).SequenceEqual([Heater]), "anchor 2");
    var detail = Need(await store.GetEntityAsync(Heater), "anchor 3 detail");
    Check(detail.ContainerId == Bedroom && detail.Path.SequenceEqual([Building, Bedroom]) && detail.PropertyForm == new FormRef("water-heater.props", "1.0.0"), "anchor 3");
    Check(await store.GetEntityAsync("entity:preview/does-not-exist") is null, "anchor 4");
    var tree = Need(await store.GetTreeAsync(Bedroom, null), "anchor 5 tree");
    Check(tree.Path.SequenceEqual([Building]) && tree.Children.Select(x => x.Id).Order(StringComparer.Ordinal).SequenceEqual(new[] { "entity:preview/hvac-1", Heater }), "anchor 5");
    var assessment = Single((await store.GetConditionHistoryAsync(Heater, null)).History, "anchor 6");
    Check(assessment.Grade == 4 && assessment.ScaleMax == 5 && assessment.SourceForm == "plumbing.inspection" && assessment.SourceField == "condition", "anchor 6");
    var created = await store.CreateEntityAsync(new("water-heater", "Attic heater", null));
    var edge = await store.AddEdgeAsync(new(EdgeKind.Contains, Bedroom, created.Id));
    Check(edge.Kind == EdgeKind.Contains && (await store.GetEntityAsync(created.Id))?.ContainerId == Bedroom, "anchor 7");
    var missing = "entity:vanished-ancestor";
    var crumbs = await new BreadcrumbResolver(store).ResolveAsync([missing, Building]);
    Check(crumbs[0].Label == BreadcrumbResolver.UnresolvedLabel && crumbs.All(x => x.Label != missing), "anchor 8");
    Check((await store.GetBoundFormsAsync("note")).Count == 0, "anchor 9");

    var forms = await store.GetBoundFormsAsync("water-heater");
    Check(forms.Any(x => x.Definition == "plumbing.inspection"), "bound-form descriptor");
    var navigation = new ViewNavigationTargets();
    Check(navigation.FillForm("plumbing.inspection", "entity:acme/heater-1", "Heater A") ==
        new FillFormNavigationTarget("/forms?form=plumbing.inspection&into=entity%3Aacme%2Fheater-1", "Heater A"), "fill target");
    var submitted = Single(await store.GetSubmittedInstancesAsync(Heater), "submitted descriptor");
    Check(navigation.ViewSubmission(submitted.FormId, submitted.InstanceId) ==
        "/forms?form=plumbing.inspection&instance=forminst%3Apreview%2Fwh-inspection-1", "submission target");

    await ProveHttpRows(scopes, clock);
}

async Task ProvePairs(DevelopmentPreviewAdapter fallback, InMemoryEntityViewsStore durable)
{
    static string Canonical<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    IEntityReadStore leftRead = fallback, rightRead = durable;
    IConditionHistoryStore leftHistory = fallback, rightHistory = durable;

    Check(Canonical(new { all = await leftRead.ListEntitiesAsync(null), filtered = await leftRead.ListEntitiesAsync("water-heater") }) ==
          Canonical(new { all = await rightRead.ListEntitiesAsync(null), filtered = await rightRead.ListEntitiesAsync("water-heater") }), "pair 1");
    Check(Canonical(new { found = await leftRead.GetEntityAsync(Heater), missing = await leftRead.GetEntityAsync("missing") }) ==
          Canonical(new { found = await rightRead.GetEntityAsync(Heater), missing = await rightRead.GetEntityAsync("missing") }), "pair 2");
    const string asOf = "2026-01-02T03:04:05.000Z";
    Check(Canonical(new { found = await leftRead.GetTreeAsync(Building, asOf), missing = await leftRead.GetTreeAsync("missing", asOf) }) ==
          Canonical(new { found = await rightRead.GetTreeAsync(Building, asOf), missing = await rightRead.GetTreeAsync("missing", asOf) }), "pair 3");
    Check(Canonical(new { condition = await leftHistory.GetConditionHistoryAsync(Heater, null), submissions = await leftHistory.GetSubmissionsAsync(Heater) }) ==
          Canonical(new { condition = await rightHistory.GetConditionHistoryAsync(Heater, null), submissions = await rightHistory.GetSubmissionsAsync(Heater) }), "pair 4");
}

async Task ProveHttpRows(EntityViewsTenantScopes scopes, TimeProvider time)
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        try { await next(); }
        catch (EntityViewsException error)
        {
            context.Response.StatusCode = error.Code == EntityViewsCodes.StoreUnavailable ? 500 : 400;
            await context.Response.WriteAsJsonAsync(new { code = error.Code, message = "The entity views request could not be completed." });
        }
    });
    EntityViewsTenantScope Scope(HttpContext c) => scopes.ForTenant(c.Request.Headers["X-Tenant"].FirstOrDefault() ?? "team-a");
    // Mirror the pinned host's effective id handling: the pinned client sends
    // encodeURIComponent(id), so an id's '/' arrives as %2F, which routing does not decode
    // into a single-segment route value. Unescape explicitly (a no-op for already-decoded ids).
    static string Id(string raw) => Uri.UnescapeDataString(raw);
    const string asset = "/api/local-node/asset-registry";

    app.MapGet($"{asset}/entities", async (HttpContext c, string? type) => Results.Json(new { entities = await Scope(c).Entities.ListEntitiesAsync(type) }));
    app.MapGet($"{asset}/entities/{{id}}", async (HttpContext c, string id) =>
        await Scope(c).Entities.GetEntityAsync(Id(id)) is { } entity ? Results.Json(entity) : Results.NotFound());
    app.MapPost($"{asset}/entities", async (HttpContext c, CreateEntityBody body) => Results.Json(await Scope(c).Entities.CreateEntityAsync(body), statusCode: 201));
    app.MapGet($"{asset}/entities/{{id}}/tree", async (HttpContext c, string id, string? asOf) =>
        await Scope(c).Entities.GetTreeAsync(Id(id), asOf) is { } tree ? Results.Json(tree) : Results.NotFound());
    app.MapGet($"{asset}/entities/{{id}}/condition", async (HttpContext c, string id, string? asOf) =>
        await Scope(c).Entities.GetEntityAsync(Id(id)) is null ? Results.NotFound() : Results.Json(await Scope(c).History.GetConditionHistoryAsync(Id(id), asOf)));
    app.MapGet($"{asset}/entities/{{id}}/submissions", async (HttpContext c, string id) =>
        await Scope(c).Entities.GetEntityAsync(Id(id)) is null ? Results.NotFound() : Results.Json(await Scope(c).History.GetSubmissionsAsync(Id(id))));
    app.MapPost($"{asset}/edges", async (HttpContext c, AddEdgeBody body) => Results.Json(await Scope(c).Entities.AddEdgeAsync(body), statusCode: 201));
    // The CancellationToken parameter is load-bearing: a lambda whose ONLY parameter is
    // HttpContext binds as a raw RequestDelegate and its IResult return is silently discarded
    // (an empty 200). A second bindable parameter forces the request-delegate-factory path.
    app.MapGet($"{asset}/types", async (HttpContext c, CancellationToken _) => Results.Json(new { types = await Scope(c).Types.GetEffectiveCatalogAsync() }));
    app.MapGet($"{asset}/types/{{id}}", async (HttpContext c, string id) =>
        await Scope(c).Types.GetTypeAsync(Id(id)) is { } type ? Results.Json(type) : Results.NotFound());
    app.MapPost($"{asset}/types", async (HttpContext c, TypeUpsertBody body) => Results.Json(await Scope(c).Types.CreateTypeAsync(body), statusCode: 201));
    app.MapPut($"{asset}/types/{{id}}", async (HttpContext c, string id, TypeUpsertBody body) =>
        await Scope(c).Types.UpdateTypeAsync(Id(id), body) is { } type ? Results.Json(type) : Results.NotFound());
    app.MapPost($"{asset}/types/{{id}}/revert", async (HttpContext c, string id) =>
        await Scope(c).Types.RevertTypeAsync(Id(id)) is { } type ? Results.Json(type) : Results.NotFound());
    app.MapPost("/api/local-node/forms/{formId}/submit", async (HttpContext c, string formId, SubmitBody body) =>
    {
        var scope = Scope(c);
        var entityId = c.Request.Headers["Into-Case-Ref"].FirstOrDefault() ?? body.AssetRef;
        if (entityId is null) return Results.BadRequest();
        var capture = await scope.ConditionCapture.CaptureFromSubmission(entityId, formId, "/condition", body.Condition, 5, time.GetUtcNow(), null, null);
        var instance = $"forminst:fixture/{Guid.NewGuid():N}";
        if (c.Request.Headers.ContainsKey("Into-Case-Ref"))
            await scope.SubmissionLinking.LinkFromCase(entityId, formId, instance, time.GetUtcNow(), null);
        return Results.Json(capture.Projection is null
            ? new Dictionary<string, object?> { ["instanceId"] = instance }
            : new Dictionary<string, object?> { ["instanceId"] = instance, ["projection"] = capture.Projection, ["skips"] = capture.Skips }, statusCode: 201);
    });
    IEntityReadStore throwingRouteStore = new ThrowingStore();
    app.MapGet("/__throw", async () => Results.Json(await throwingRouteStore.GetEntityAsync("route-failure")));

    await app.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("X-Tenant", "team-a");
        var proven = 0;
        async Task<JsonElement> Body(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        async Task<HttpResponseMessage> Post(string path, object value) => await client.PostAsJsonAsync(path, value, json);
        async Task<HttpResponseMessage> Put(string path, object value) => await client.PutAsJsonAsync(path, value, json);
        void Status(HttpResponseMessage response, HttpStatusCode expected, string row) { Check(response.StatusCode == expected, row); proven++; }
        async Task<string> Create(string name)
        {
            var response = await Post($"{asset}/entities", new CreateEntityBody("water-heater", name, null));
            Check(response.StatusCode == HttpStatusCode.Created, "create helper");
            return (await Body(response)).GetProperty("id").GetString()!;
        }

        var r1 = await client.GetAsync($"{asset}/types"); Status(r1, HttpStatusCode.OK, "route 1"); Check((await Body(r1)).GetProperty("types").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "water-heater"), "route 1 body");
        var id2 = await Create("Unit 4B water heater"); var get2 = await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(id2)}"); Check(get2.IsSuccessStatusCode, "route 2 get"); var list2 = await Body(await client.GetAsync($"{asset}/entities?type=water-heater")); Check(list2.GetProperty("entities").EnumerateArray().Any(x => x.GetProperty("id").GetString() == id2), "route 2 list"); proven++;
        var parent = await Create("Building A"); var child = await Create("Water heater"); var r3 = await Post($"{asset}/edges", new AddEdgeBody(EdgeKind.Contains, parent, child)); Status(r3, HttpStatusCode.Created, "route 3"); var tree3 = await Body(await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(parent)}/tree")); Check(tree3.GetProperty("children").EnumerateArray().Any(x => x.GetProperty("id").GetString() == child), "route 3 body");
        var r4 = await Post($"{asset}/edges", new AddEdgeBody(EdgeKind.Contains, parent, "secret-payload")); Status(r4, HttpStatusCode.BadRequest, "route 4"); var text4 = await r4.Content.ReadAsStringAsync(); Check(text4.Contains(EntityViewsCodes.UnknownEntity) && !text4.Contains("secret-payload"), "route 4 static body");
        var id5 = await Create("inspect"); var r5 = await Post("/api/local-node/forms/condition-form/submit", new { condition = 4, assetRef = id5 }); Status(r5, HttpStatusCode.Created, "route 5"); var h5 = await Body(await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(id5)}/condition")); Check(h5.GetProperty("history")[0].GetProperty("sourceField").GetString() == "/condition", "route 5 body");
        var id6 = await Create("wide bad"); var r6 = await Post("/api/local-node/forms/wide-condition-form/submit", new { condition = 8, assetRef = id6 }); Status(r6, HttpStatusCode.Created, "route 6"); var b6 = await Body(r6); Check(b6.GetProperty("projection").GetString() == "skipped" && !b6.GetProperty("skips")[0].TryGetProperty("value", out _), "route 6 body");
        var id7 = await Create("wide good"); var r7 = await Post("/api/local-node/forms/wide-condition-form/submit", new { condition = 4, assetRef = id7 }); Status(r7, HttpStatusCode.Created, "route 7"); var b7 = await Body(r7); Check(!b7.TryGetProperty("projection", out _) && !b7.TryGetProperty("skips", out _), "route 7 body");
        var id8 = await Create("case linked"); using var q8 = new HttpRequestMessage(HttpMethod.Post, "/api/local-node/forms/case-condition-form/submit") { Content = JsonContent.Create(new { condition = 3 }) }; q8.Headers.Add("Into-Case-Ref", id8); var r8 = await client.SendAsync(q8); Status(r8, HttpStatusCode.Created, "route 8"); Check((await Body(await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(id8)}/submissions"))).GetProperty("submissions").GetArrayLength() == 1, "route 8 link");
        var id9 = await Create("field linked"); using var q9 = new HttpRequestMessage(HttpMethod.Post, "/api/local-node/forms/condition-form/submit") { Content = JsonContent.Create(new { condition = 4, assetRef = id9 }) }; q9.Headers.Add("Into-Case-Ref", id9); var r9 = await client.SendAsync(q9); Status(r9, HttpStatusCode.Created, "route 9"); Check((await Body(await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(id9)}/submissions"))).GetProperty("submissions").GetArrayLength() == 1, "route 9 body");
        var id10 = await Create("not linked"); var r10 = await Post("/api/local-node/forms/condition-form/submit", new { condition = 4, assetRef = id10 }); Status(r10, HttpStatusCode.Created, "route 10"); Check((await Body(await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(id10)}/submissions"))).GetProperty("submissions").GetArrayLength() == 0, "route 10 body");
        var r11 = await client.GetAsync($"{asset}/entities/does-not-exist/submissions"); Status(r11, HttpStatusCode.NotFound, "route 11"); Check((await r11.Content.ReadAsStringAsync()).Length == 0, "route 11 opaque");
        var teamA = await Create("team A"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-b"); var r12 = await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(teamA)}/submissions"); Status(r12, HttpStatusCode.NotFound, "route 12"); Check((await r12.Content.ReadAsStringAsync()).Length == 0, "route 12 opaque"); var r13 = await client.GetAsync($"{asset}/entities/{Uri.EscapeDataString(teamA)}"); Status(r13, HttpStatusCode.NotFound, "route 13"); Check((await r13.Content.ReadAsStringAsync()).Length == 0, "route 13 opaque"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-a");
        var shed = new TypeUpsertBody("shed", "Storage shed", ["container", "maintainable"], null, null, null, null, 4, null, null); var r14 = await Post($"{asset}/types", shed); Status(r14, HttpStatusCode.Created, "route 14"); Check((await Body(r14)).GetProperty("provenance").GetString() == "Tenant", "route 14 body");
        var edit = new TypeUpsertBody(null, "Boiler (AcmeCo term)", ["maintainable", "movable"], null, null, null, null, null, null, null); var r15 = await Put($"{asset}/types/water-heater", edit); Status(r15, HttpStatusCode.OK, "route 15"); Check((await Body(r15)).GetProperty("overridesSeed").GetBoolean(), "route 15 override"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-b"); Check((await Body(await client.GetAsync($"{asset}/types/water-heater"))).GetProperty("provenance").GetString() == "Pack", "route 15 seed immutable"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-a");
        var r16 = await client.PostAsync($"{asset}/types/water-heater/revert", null); Status(r16, HttpStatusCode.OK, "route 16"); Check(!(await Body(r16)).GetProperty("overridesSeed").GetBoolean(), "route 16 body");
        var widget = new TypeUpsertBody("acme-widget", "Acme widget", ["maintainable"], null, null, null, null, null, null, null); Check((await Post($"{asset}/types", widget)).StatusCode == HttpStatusCode.Created, "route 17 setup"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-b"); var r17 = await client.GetAsync($"{asset}/types/acme-widget"); Status(r17, HttpStatusCode.NotFound, "route 17"); Check((await r17.Content.ReadAsStringAsync()).Length == 0, "route 17 opaque"); client.DefaultRequestHeaders.Remove("X-Tenant"); client.DefaultRequestHeaders.Add("X-Tenant", "team-a");
        var rich = new TypeUpsertBody(null, "Water heater", ["maintainable", "movable"], null, ["plumbing", "hvac"], new("wh-props", "2.1.0"), [new("plumbing", "wh-plumbing-insp", "1.0.0"), new("hvac", "wh-hvac-insp", "1.2.0")], 5, 12, new(1400.50m, "AED")); var r18 = await Put($"{asset}/types/water-heater", rich); Status(r18, HttpStatusCode.OK, "route 18"); var b18 = await Body(r18); Check(b18.GetProperty("inspectionForms").GetArrayLength() == 2 && b18.GetProperty("typicalReplacementCost").GetProperty("currency").GetString() == "AED", "route 18 body");
        var badVersion = new TypeUpsertBody(null, "Water heater", ["maintainable"], null, null, new("wh-props", "not-a-version"), null, null, null, null); var r19 = await Put($"{asset}/types/water-heater", badVersion); Status(r19, HttpStatusCode.BadRequest, "route 19"); Check((await Body(r19)).GetProperty("code").GetString() == EntityViewsTypeCodes.InvalidFormVersion, "route 19 code");
        var noTraits = new TypeUpsertBody("no-traits", "No traits", [], null, null, null, null, null, null, null); var r20 = await Post($"{asset}/types", noTraits); Status(r20, HttpStatusCode.BadRequest, "route 20"); Check((await Body(r20)).GetProperty("code").GetString() == EntityViewsTypeCodes.AtLeastOneTraitRequired, "route 20 code");
        Check(proven == 20, "all twenty route rows proved");

        var failure = await client.GetAsync("/__throw");
        Check(failure.StatusCode == HttpStatusCode.InternalServerError, "non-404 failure must be 500");
    }
    finally { await app.StopAsync(); await app.DisposeAsync(); }
}

EntityViewsTenantScopes MakeScopes(TimeProvider clock)
{
    var seed = new TypeDetailWire("water-heater", "Water Heater", ["maintainable", "movable"], null, ["plumbing"], "Pack", false, true, false,
        new("water-heater.props", "1.0.0"), [new("plumbing", "plumbing.inspection", "1.0.0")], 5, 12, new(1400m, "USD"));
    var entities = new[] { new EntityDetail(Building, "building", "Harborline House", null, "2026-07-16T00:00:00.000Z", null, null, [], null) };
    return new EntityViewsTenantScopes(entities, null, null, null, null, ["building", "water-heater"], new InMemoryTypeCatalogStore([seed]), clock, value => ValueTask.FromResult<string?>(value));
}

InMemoryEntityViewsStore DurableStandIn(TimeProvider clock)
{
    var now = "2026-07-16T00:00:00.000Z";
    EntityDetail[] entities = [
        new(Building, "building", "Harborline House", null, now, null, null, [], null),
        new(Bedroom, "bedroom", "Primary bedroom", null, now, null, Building, [Building], null),
        new(Heater, "water-heater", "Water heater — closet", null, now, null, Bedroom, [Building, Bedroom], new("water-heater.props", "1.0.0")),
        new("entity:preview/hvac-1", "hvac-condenser", "HVAC condenser — north side", null, now, null, Bedroom, [Building, Bedroom], null)];
    ConditionHistory[] conditions = [new(Heater, [new("cond:preview/1", Heater, 4, 5, null, .8, now, null, "plumbing.inspection", "condition", null)])];
    SubmissionList[] submissions = [new(Heater, [new("forminst:preview/wh-inspection-1", "plumbing.inspection", now, null)])];
    KeyValuePair<string, IReadOnlyList<BoundFormDescriptor>>[] bindings = [new("water-heater", [new("plumbing.inspection", "1.0.0", "plumbing"), new("water-heater.props", "1.0.0", "Property form")])];
    return new(entities, null, conditions, submissions, bindings, clock, ["building", "bedroom", "water-heater", "hvac-condenser", "boiler"]);
}

static void ProveAssemblyClosure()
{
    _ = typeof(Harborline.Contracts.Forms.FormDefinition);
    var closure = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(x => x.Name ?? "").Concat(AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name ?? "")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    Check(!closure.Any(x => x.Contains("Forms.Builder", StringComparison.OrdinalIgnoreCase) || x.Contains("Forms.Authoring", StringComparison.OrdinalIgnoreCase)), "forms builder/authoring dependency pollution");
}

static void ProveProductionGuard(TimeProvider clock)
{
    try { _ = new DevelopmentPreviewAdapter("Production", clock); throw new Exception("production preview did not fail closed"); }
    catch (EntityViewsException e) { Check(e.Code == EntityViewsCodes.ProductionPreviewForbidden, "production preview code"); }
}

static async Task ProveFailureAndBreadcrumbGuards(TimeProvider clock)
{
    var failure = new ThrowingStore();
    try { _ = await failure.GetEntityAsync("x"); throw new Exception("throwing store returned absence"); }
    catch (EntityViewsException e) { Check(e.Code == EntityViewsCodes.StoreUnavailable, "store failure code"); }
    var raw = "entity:raw-secret";
    var crumbs = await new BreadcrumbResolver(failure.WithUnknowns()).ResolveAsync([raw]);
    Check(crumbs.Single().Label == BreadcrumbResolver.UnresolvedLabel && crumbs.Single().Label != raw, "raw-id guard");
}

static T Need<T>(T? value, string message) where T : class => value ?? throw new Exception(message);
static T Single<T>(IReadOnlyList<T> values, string message) => values.Count == 1 ? values[0] : throw new Exception(message);
static void Check(bool condition, string message) { if (!condition) throw new Exception($"VIEWS FIXTURE FAILED: {message}"); }

sealed record SubmitBody(int Condition, string? AssetRef);
sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider { public override DateTimeOffset GetUtcNow() => instant; }
sealed class ThrowingStore : IEntityReadStore
{
    public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => throw Failure();
    public ValueTask<EntityDetail?> GetEntityAsync(string id) => throw Failure();
    public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => throw Failure();
    public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => throw Failure();
    public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => throw Failure();
    public IEntityReadStore WithUnknowns() => new UnknownStore();
    static EntityViewsException Failure() => new(EntityViewsCodes.StoreUnavailable, "Store unavailable.");
    sealed class UnknownStore : IEntityReadStore
    {
        public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => ValueTask.FromResult<IReadOnlyList<EntitySummary>>([]);
        public ValueTask<EntityDetail?> GetEntityAsync(string id) => ValueTask.FromResult<EntityDetail?>(null);
        public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => ValueTask.FromResult<TreeView?>(null);
        public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => throw new NotSupportedException();
        public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => throw new NotSupportedException();
    }
}
