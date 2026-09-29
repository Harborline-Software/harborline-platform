using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class AuthorizationTraceTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-18T12:00:00Z");

    [Fact]
    public async Task Reads_stored_evidence_and_counterfactual_after_revocation_without_replaying_act()
    {
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1", new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), At);
        var facts = new List<string> { "roster:original" };
        var evidence = new AuthorizationDecisionEvidence(request, true, "None", "grant:1", [], [], facts);
        facts.Clear();
        var counterfactual = new AuthorizationCounterfactualSnapshot(1, "GrantRevocation", "Deny", "1", 0, "Revoke grant 1");
        var stored = new AuthorizationTraceSnapshot("a", "alice", 2, evidence.Project(), counterfactual);
        var gate = new ReadGate();
        var reader = new AuthorizationTraceReader(new Store(stored), gate);
        var read = await reader.ReadAsync("a", "alice", "entry", At.AddDays(1));
        Assert.Equal(AuthorizationTraceAvailability.Available, read.Availability);
        Assert.Equal(new[] { "act", "effective-roles", "standings", "verdict" }, read.Steps.Select(step => step.Stage));
        Assert.Contains("roster:original", read.Steps[1].Facts);
        Assert.Contains("verdict:allowed", read.Steps[3].Facts);
        Assert.Equal(counterfactual, read.Counterfactual);
        Assert.Equal("audit:trace-read", Assert.Single(gate.Requests).Operation);
        Assert.Equal(At.AddDays(1), gate.Requests[0].At);
    }

    [Fact]
    public async Task Unauthorized_reads_hide_existence_and_predecision_refusal_is_distinct_from_absence()
    {
        var refusal = new AuthorizationPreDecisionRefusal("guard", "No admitted context", "Authenticate");
        var store = new Store(new("a", "alice", null, [], null, refusal));
        var gate = new ReadGate { Allowed = false };
        var reader = new AuthorizationTraceReader(store, gate);
        foreach (var id in new[] { "entry", "missing" })
        {
            var hidden = await reader.ReadAsync("a", "mallory", id, At);
            Assert.Equal(AuthorizationTraceAvailability.Refused, hidden.Availability);
            Assert.Empty(hidden.Steps);
            Assert.Null(hidden.Version);
            Assert.Null(hidden.Counterfactual);
            Assert.Null(hidden.Refusal);
        }
        gate.Allowed = true;
        var recorded = await reader.ReadAsync("a", "auditor", "entry", At);
        Assert.Equal(AuthorizationTraceAvailability.PreDecisionRefusal, recorded.Availability);
        Assert.Equal(refusal, recorded.Refusal);
        Assert.Empty(recorded.Steps);
        Assert.Equal(AuthorizationTraceAvailability.NotAvailable, (await reader.ReadAsync("a", "auditor", "missing", At)).Availability);
        Assert.Equal(AuthorizationTraceAvailability.NotAvailable, (await reader.ReadAsync("b", "auditor", "entry", At)).Availability);
    }

    [Fact]
    public async Task No_recorded_decision_stays_absent_and_approval_ordinals_do_not_replace_gate_steps()
    {
        var gate = new ReadGate();
        var absent = await new AuthorizationTraceReader(new Store(new("a", "alice", null, [])), gate)
            .ReadAsync("a", "auditor", "missing", At);
        Assert.Equal(AuthorizationTraceAvailability.NotAvailable, absent.Availability);
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1", new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), At);
        var evidence = new AuthorizationDecisionEvidence(request, true, "None", "grant:1", [], []);
        var steps = evidence.Project().Concat(evidence.Project().Select(step => new AuthorizationTraceStep(step.Ordinal + 4, step.Stage, ["approval:denied"])));
        var read = await new AuthorizationTraceReader(new Store(new("a", "alice", 2, steps)), gate)
            .ReadAsync("a", "auditor", "entry", At);
        Assert.Equal(4, read.Steps.Count);
        Assert.Contains("verdict:allowed", read.Steps[3].Facts);
        Assert.DoesNotContain(read.Steps, step => step.Facts.Contains("approval:denied"));
    }

    [Fact]
    public async Task Present_malformed_trace_is_distinct_from_absence()
    {
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), At);
        var steps = new AuthorizationDecisionEvidence(request, true, "None", "grant:1", [], []).Project();
        AuthorizationTraceSnapshot[] malformed =
        [
            new("a", "alice", 2, [new(0, "act", []), .. steps.Skip(1)]),
            new("a", "alice", 2, [.. steps.Take(2), new AuthorizationTraceStep(3, "wrong", []), steps[3]]),
            new("a", "alice", null, steps),
            new("a", "alice", 2, [.. steps, steps[3]]),
            // A step outside the decision (1..4) and approval (5..8) ordinals is malformed, not ignored.
            new("a", "alice", 2, [.. steps, new AuthorizationTraceStep(0, "act", [])]),
            new("a", "alice", 2, [.. steps, new AuthorizationTraceStep(9, "act", [])]),
            // Only evidence versions 1 through CurrentVersion exist.
            new("a", "alice", 0, steps),
            new("a", "alice", AuthorizationDecisionEvidence.CurrentVersion + 1, steps),
        ];

        foreach (var snapshot in malformed)
        {
            var reader = new AuthorizationTraceReader(new Store(snapshot), new ReadGate());
            var read = await reader.ReadAsync("a", "auditor", "entry", At);
            Assert.Equal(AuthorizationTraceAvailability.Malformed, read.Availability);
            Assert.Null(read.Version);
            Assert.Empty(read.Steps);
            Assert.Null(read.Counterfactual);
            Assert.Equal(AuthorizationTraceAvailability.NotAvailable,
                (await reader.ReadAsync("a", "auditor", "missing", At)).Availability);
        }
    }

    [Fact]
    public void Decision_evidence_projects_host_facts_and_role_description_without_recalculation()
    {
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), At);
        var role = new AuthorizationEvidenceRole("reviewer", "region:north", At, null, "binding", null,
            "definition", "approve", null, false);
        var evidence = new AuthorizationDecisionEvidence(request, false, "SeparationRequired", "binding:1",
            [role],
            [new("reviewer", "standing", "v1")], ["binding:host-fact"], "GrantRefused",
            "Gate", null, "custom-target", "records:approve");

        var steps = evidence.Project();

        Assert.Equal(["kind:Gate", "act:records:approve", "target:custom-target", "principal:alice", "tenant:a"],
            steps[0].Facts.Take(5).ToArray());
        Assert.Equal(request.At, evidence.At);
        Assert.StartsWith("role:reviewer;scope:region:north;valid:", steps[1].Facts[0]);
        Assert.Contains("..open;binding:binding@-;definition:definition;atom:approve;in-force:not-computed", steps[1].Facts[0]);
        Assert.Equal(["deciding:binding:1", "binding:host-fact"], steps[1].Facts.Skip(1).ToArray());
        Assert.Equal(["role:reviewer;rule:standing;evidence:v1"], steps[2].Facts.ToArray());
        Assert.Equal(["verdict:denied", "refusal:SeparationRequired", "version:2", "grant-refusal:GrantRefused"], steps[3].Facts.ToArray());
    }

    [Fact]
    public async Task Custom_principal_comparer_selects_the_self_trace_read_operation()
    {
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), At);
        var snapshot = new AuthorizationTraceSnapshot("a", "ALICE", 2,
            new AuthorizationDecisionEvidence(request, true, "None", "binding:1", [], []).Project());
        var gate = new ReadGate();

        var read = await new AuthorizationTraceReader(new Store(snapshot), gate, StringComparer.OrdinalIgnoreCase)
            .ReadAsync("a", "alice", "entry", At);

        Assert.Equal(AuthorizationTraceAvailability.Available, read.Availability);
        Assert.Equal("audit:trace-read", Assert.Single(gate.Requests).Operation);
    }

    private sealed class Store(AuthorizationTraceSnapshot snapshot) : IAuthorizationTraceStore
    {
        public ValueTask<AuthorizationTraceSnapshot?> FindAsync(string tenant, string entryId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(entryId == "entry" ? snapshot : null);
    }

    private sealed class ReadGate : IAuthorizationDecider
    {
        public bool Allowed { get; set; } = true;
        public List<AccessRequest> Requests { get; } = [];
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Assert.StartsWith("audit:", request.Operation);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, Allowed, "NoEffectiveRole", "grant:auditor", [], []));
        }
    }
}
