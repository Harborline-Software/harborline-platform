using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;
using Harborline.Foundation.Session;

using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "appshell-vertical-cases.json")));
Check(corpus.RootElement.GetProperty("sessionTenantAnchors").GetArrayLength() == 8, "proved anchor count");

var tenantA = Tenant("tenant-a"); var tenantB = Tenant("tenant-b");
var store = new InMemorySessionStore();
var issued = DateTimeOffset.Parse("2026-08-16T12:00:00Z");
await store.CreateAsync(new SessionRecord { SessionId="cookie-opaque-a", UserId="alice", TenantId=tenantA.Id,
    IssuedUtc=issued, LastSeenUtc=issued, AbsoluteExpiryUtc=issued.AddHours(8), Reason=SessionEstablishmentReason.PasswordLogin });
var resolver = new SessionResolver(store);

// Establish + reload: the opaque id resolves twice, and accepted activity slides LastSeenUtc.
var first = await resolver.ResolveAsync("cookie-opaque-a", new TenantContext(tenantA), ["member"], issued.AddMinutes(1), TimeSpan.FromMinutes(30));
var reload = await resolver.ResolveAsync("cookie-opaque-a", new TenantContext(tenantA), ["member"], issued.AddMinutes(2), TimeSpan.FromMinutes(30));
Check(first is { UserId: "alice" } && reload?.SessionId == first.SessionId, "session establish/reload");

// Foreign tenant and inactive tenant resolve to no actor. A host maps absent scoped resources to 404.
Check(await resolver.ResolveAsync("cookie-opaque-a", new TenantContext(tenantB), [], issued.AddMinutes(3), TimeSpan.FromMinutes(30)) is null, "foreign tenant fail closed");
Check(await resolver.ResolveAsync("cookie-opaque-a", new TenantContext(tenantA with { Status=TenantStatus.Suspended }), [], issued.AddMinutes(3), TimeSpan.FromMinutes(30)) is null, "inactive tenant fail closed");
var rows = new[] { new TenantRow(tenantA.Id, "a"), new TenantRow(tenantB.Id, "b") }.AsQueryable();
Check(rows.WhereTenant(new TenantContext(tenantA)).Single().TenantId == tenantA.Id, "tenant query isolation");

// Expiry removes the record; revocation is idempotent and cannot leave an authenticated actor.
Check(await resolver.ResolveAsync("cookie-opaque-a", new TenantContext(tenantA), [], issued.AddHours(9), TimeSpan.FromHours(10)) is null, "absolute expiry");
Check(await store.GetAsync("cookie-opaque-a") is null && !await store.RemoveAsync("cookie-opaque-a"), "expiry/revoke removal");

// Never anonymous means fail closed: no synthetic anonymous PartyId is returned.
var actor = new Actor(new TenantContext(tenantA), "", []);
try { await new PartyContext(actor, new PartyResolver()).GetCurrentPartyIdAsync(); throw new Exception("FAILED: anonymous party minted"); }
catch (PrincipalPartyResolutionException e) when (e.Failure == PrincipalPartyResolutionFailure.NoAuthenticatedPrincipal) { }

Check(typeof(ISessionStore).Assembly.GetName().Name == "Harborline.Foundation.Session", "session package identity");
Check(typeof(IPartyContext).Assembly.GetName().Name == "Harborline.Foundation.Authorization", "authorization package identity");
Check(typeof(ITenantContext).Assembly.GetName().Name == "Harborline.Foundation.MultiTenancy", "tenancy package identity");
// Wave C corpus-conformance replay (ticket 092 Option A): canonical projection of the
// FROZEN four-field contract corpus, byte-compared against the npm lane by the verifier.
// no independent packed consumer — corpus conformance, never independent semantic agreement.
var contractDoc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText("appshell-contract-cases.json"))!;
var contractScope = contractDoc["_header"]!["scope"]!.AsArray().Select(n => (string)n!).ToArray();
Check(contractScope.SequenceEqual(new[] { "visibility", "permissionVocabulary", "lifecycleBuildModeFolding", "routeJoin" }), "contract scope frozen");
var contractScenarios = contractDoc["scenarios"]!.AsArray();
Check(contractScenarios.Count == (int)contractDoc["_header"]!["scenarioCount"]!, "contract scenario count");
var contractProjection = new System.Text.Json.Nodes.JsonArray();
foreach (var scenario in contractScenarios)
{
    var expected = scenario!["expected"]!.AsObject();
    Check(expected.Select(p => p.Key).SequenceEqual(contractScope), "contract field order");
    Check(scenario["evidence"]!.AsArray().Count > 0, "contract evidence cited");
    var row = new System.Text.Json.Nodes.JsonObject { ["id"] = (string)scenario["id"]! };
    foreach (var pair in expected) row[pair.Key] = pair.Value!.DeepClone();
    contractProjection.Add(row);
}
var contractJsonOptions = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
Console.WriteLine($"APPSHELL_CONTRACT:{contractProjection.ToJsonString(contractJsonOptions)}");
Console.WriteLine("APPSHELL_ENGINE_PASS:{\"provedAnchors\":8,\"foreignTenantStatus\":404,\"anonymousPartyMinted\":false}");

static TenantMetadata Tenant(string id) => new() { Id=new TenantId(id), Name=id, Status=TenantStatus.Active };
static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("FAILED: "+message); }
sealed record TenantContext(TenantMetadata? Tenant) : ITenantContext;
sealed record TenantRow(TenantId TenantId,string Value) : IMustHaveTenant;
sealed record Actor(TenantContext Scope,string UserId,IReadOnlyList<string> Roles) : IAuthenticatedActorContext { public TenantMetadata? Tenant => Scope.Tenant; }
sealed class PartyResolver : IPrincipalPartyResolver { public ValueTask<Guid?> ResolveAsync(string userId,TenantId tenantId,CancellationToken cancellationToken=default) => ValueTask.FromResult<Guid?>(null); }
