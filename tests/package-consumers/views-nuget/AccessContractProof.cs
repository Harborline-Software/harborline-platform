using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Authorization;

// Host boundary fixture: uses the existing role resolver and scope evaluator with effective-dated
// grants. The package providers are production AccessProvider, AccessScopeEvaluator and trace reader.
internal sealed class FixtureAuthorizationGate : IAuthorizationDecider
{
    public static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
    private static readonly RoleReference Reader = RoleReference.Domain("reader");
    private static readonly RoleVocabulary Vocabulary = RoleVocabulary.FromApi([
        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Reader, "Reader", new(RoleOwnerKind.Package, "test"), false),
    ]);
    public bool Revoked { get; set; }
    public DateTimeOffset ValidFrom { get; set; } = Epoch.AddDays(-1);
    public DateTimeOffset ValidUntil { get; set; } = Epoch.AddDays(1);
    public string Scope { get; set; } = "north";
    public List<AccessRequest> Requests { get; } = [];

    public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        var inForce = !Revoked && request.At >= ValidFrom && request.At < ValidUntil;
        var scope = new AccessScopeEvaluator(_ => new Harborline.Foundation.RuleEngine.GuardEvaluator()).Evaluate(
            "{\"and\":[{\"==\":[{\"var\":\"record.owner\"},{\"var\":\"principal\"}]},{\"==\":[{\"var\":\"record.region\"}," + System.Text.Json.JsonSerializer.Serialize(Scope) + "]}]}",
            request, new(request.Principal, request.Tenant, request.Record.Kind, request.Record.Id, request.At,
                ["record.owner", "record.region", "principal"]), cancellationToken);
        var audit = request.Operation is "audit:read" or "audit:trace-read";
        var open = request.Operation is "work:read" or "work.open";
        var held = audit ? request.Principal == "auditor" : inForce && (open ? request.Principal == "party:operator-1" : scope.Allowed);
        var allowed = RoleGateResolver.Allows(new([Reader]), Vocabulary, new(held ? [Reader] : []));
        return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed,
            allowed ? "None" : "NoEffectiveRole", allowed ? "grant:fixture@1" : "none",
            held ? [new(Reader.Name, Scope, ValidFrom, ValidUntil, "fixture", 1, "reader", request.Operation, true, allowed)] : [], []));
    }
}

internal static class AccessContractProof
{
    public static async Task RunAsync()
    {
        var gate = new FixtureAuthorizationGate();
        var access = new AccessProvider(gate);
        AccessRecord[] rows = [Row("1", "alice", 10), Row("2", "bob", 1000), Row("3", "alice", 20), Row("4", "bob", 2000), Row("5", "alice", 30)];
        foreach (var consumer in new[] { "view", "report-basis", "export", "dry-run" })
        {
            var predicate = access.Bind("records:read", "alice", "a", "work", FixtureAuthorizationGate.Epoch);
            var visible = await predicate.FilterAsync(rows, row => row);
            Check(visible.Count == 3, consumer + " count");
            Check(visible.Sum(row => row.Fields["amount"]!.GetValue<int>()) == 60, consumer + " sum");
            Check(visible.GroupBy(row => row.Fields["owner"]!.GetValue<string>()).Single().Count() == 3, consumer + " groups");
            Check(visible.Skip(1).Take(2).Select(row => row.Id).SequenceEqual(["3", "5"]), consumer + " page");
            // Executable mutation witness: filter-after-page cannot satisfy the same result.
            var mutant = await predicate.FilterAsync(rows.Skip(1).Take(2), row => row);
            Check(!mutant.Select(row => row.Id).SequenceEqual(["3", "5"]), consumer + " filter-after-page mutation survived");
        }

        var write = new AccessRequest("records:write", "alice", "a", rows[0], FixtureAuthorizationGate.Epoch);
        Check((await access.CheckAsync(write)).Allowed, "stage one admitted");
        gate.Revoked = true;
        var commits = 0;
        var checkedWrite = await access.ValidateAsync(write with { At = write.At.AddSeconds(1) }, (_, _) => ValueTask.FromResult(new AccessCheck(true, "valid")));
        if (checkedWrite.Allowed) commits++;
        Check(commits == 0 && checkedWrite.Reason == "NoEffectiveRole", "predicate-time revocation");
        gate.Revoked = false;
        Check((await access.ValidateAsync(write, (at, _) =>
        {
            Check(at == write.At, "validation instant");
            return ValueTask.FromResult(new AccessCheck(true, "valid"));
        })).Allowed, "permitted validation control");

        // Deterministically generated scopes and instants pin projection to the enforcing answer.
        foreach (var scope in new[] { "north", "south" })
        foreach (var owner in new[] { "alice", "bob" })
        foreach (var offset in new[] { -2, -1, 0, 1, 2 })
        {
            gate.Scope = scope;
            var request = write with { Principal = owner, At = FixtureAuthorizationGate.Epoch.AddDays(offset) };
            var decision = await gate.DecideAsync(request);
            Check(decision.Allowed == (owner == "alice" && scope == "north" && offset is -1 or 0), "generated enforcing verdict");
            var steps = decision.Project();
            Check(steps.Select(step => step.Stage).SequenceEqual(["act", "effective-roles", "standings", "verdict"]), "four trace steps");
            Check(steps[3].Facts.Contains(decision.Allowed ? "verdict:allowed" : "verdict:denied"), "trace equals enforcing verdict");
        }
        gate.Scope = "north";
        var original = await gate.DecideAsync(write);
        var counterfactual = new AuthorizationCounterfactualSnapshot(1, "GrantRevocation", "Deny", "fixture", 0, "Revoke the grant");
        var store = new FixtureTraceStore(new("a", "alice", 2, original.Project(), counterfactual));
        var reader = new AuthorizationTraceReader(store, gate);
        gate.Revoked = true;
        var beforeRead = gate.Requests.Count;
        var historical = await reader.ReadAsync("a", "auditor", "entry", write.At.AddDays(2));
        Check(historical.Availability == AuthorizationTraceAvailability.Available && historical.Counterfactual == counterfactual
            && historical.Steps[3].Facts.Contains("verdict:allowed"), "historical evidence immutable after revocation");
        Check(gate.Requests.Skip(beforeRead).Single().Operation == "audit:read", "no historical replay");
        foreach (var id in new[] { "entry", "missing" })
        {
            var hidden = await reader.ReadAsync("a", "mallory", id, write.At);
            Check(hidden.Availability == AuthorizationTraceAvailability.Refused && hidden.Steps.Count == 0
                && hidden.Counterfactual is null && hidden.Refusal is null && hidden.Version is null, "opaque read refusal");
        }
        Check((await reader.ReadAsync("a", "auditor", "missing", write.At)).Availability == AuthorizationTraceAvailability.NotAvailable, "absent decision");
    }

    private static AccessRecord Row(string id, string owner, int amount) => new("a", "work", id,
        new Dictionary<string, JsonNode?> { ["owner"] = JsonValue.Create(owner), ["region"] = JsonValue.Create("north"), ["amount"] = JsonValue.Create(amount) });
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ACCESS CONTRACT FAILED: " + message);
    }
    private sealed class FixtureTraceStore(AuthorizationTraceSnapshot snapshot) : IAuthorizationTraceStore
    {
        public ValueTask<AuthorizationTraceSnapshot?> FindAsync(string tenant, string entryId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tenant == snapshot.Tenant && entryId == "entry" ? snapshot : null);
    }
}
