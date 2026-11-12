using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.Workflow.Durable;
using Harborline.Contracts.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Harborline.Blocks.Workflow.Interpreter.Tests;

public sealed partial class DeclarativeWorkflowInterpreterTests
{
    // Oracle: the two literal declarations and stored identity, independently specified by the slice ruling.
    private static JsonElement StandingDefinition(string kind = "record", string collection = "transitions")
    {
        var root = JsonNode.Parse(CpAuthored().GetRawText())!;
        root["subject"] = new JsonObject { [kind] = new JsonObject { ["recordType"] = "invoice" } };
        return Gate(JsonSerializer.SerializeToElement(root), collection,
            collection == "transitions" ? "t-approve" : "a-post-je", "requiredStandings",
            new JsonArray("assigned-approver", "independent-reviewer"));
    }

    private static WorkflowInstanceRecord StandingInstance() => new()
    {
        Id = "standing-1", TenantId = Tenant, DefinitionKey = CpKey, DefinitionVersion = Version,
        CurrentStep = "PendingApproval", Status = WorkflowStatus.Parked,
        SubjectRecord = new("invoice", "invoice-42"),
        StateJson = """{"recordId":"forged-state","subjectRecord":{"recordType":"other","recordId":"forged-state"}}""",
    };

    [Theory]
    [InlineData(false, "transitions")]
    [InlineData(true, "transitions")]
    [InlineData(false, "actions")]
    [InlineData(true, "actions")]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_host_aggregate_answer_controls_transition_and_action(bool satisfied, string collection)
    {
        var gate = new RecordingGate(satisfied);
        using var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, StandingDefinition(collection: collection), CpKey);
        var trigger = WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "standing-1", "PendingApproval",
            """{"decision":"approve","requiredStandings":[],"standings":["approver"],"recordId":"forged-trigger","subjectRecord":{"recordType":"other","recordId":"forged-trigger"}}""");
        if (satisfied)
        {
            var result = await Interpreter(sp).DecideAsync(StandingInstance(), trigger, Authority("standing-1"));
            Assert.Equal("Posted", result.NextStep);
            Assert.NotNull(result.Effect);
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
                await Interpreter(sp).DecideAsync(StandingInstance(), trigger, Authority("standing-1")));
            Assert.Equal("workflow.gate.denied", refusal.Code);
        }
        var asked = Assert.Single(gate.Asked);
        Assert.Equal([new RecordStandingReference("assigned-approver"), new RecordStandingReference("independent-reviewer")], asked.RequiredStandings);
        Assert.Equal(new WorkflowSubjectRecordReference("invoice", "invoice-42"), asked.SubjectRecord);
        Assert.Empty(asked.RequiredRoles);
        Assert.Equal(collection == "transitions" ? WorkflowGateElement.Transition : WorkflowGateElement.Action, asked.Element);
        Assert.Equal(satisfied ? 1 : 0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    [Theory]
    [InlineData("record-id")]
    [InlineData("record-type")]
    [InlineData("missing-subject")]
    [InlineData("standing")]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_an_answer_for_another_subject_or_standing_is_refused(string field)
    {
        var gate = new RecordingGate(true, i => field switch
        {
            "record-id" => i with { SubjectRecord = new("invoice", "other") },
            "record-type" => i with { SubjectRecord = new("other", "invoice-42") },
            "missing-subject" => i with { SubjectRecord = null },
            _ => i with { RequiredStandings = [new("assigned-approver")] },
        });
        using var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, StandingDefinition(), CpKey);
        var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
            await Interpreter(sp).DecideAsync(StandingInstance(), Human("standing-1", "PendingApproval", "approve"), Authority("standing-1")));
        Assert.Equal("workflow.gate.answer_unbound", refusal.Code);
        Assert.Equal(0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    [Fact]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_set_subject_cannot_use_a_single_record_standing()
    {
        var gate = new RecordingGate(true);
        using var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, StandingDefinition("set"), CpKey);
        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await Interpreter(sp).DecideAsync(StandingInstance(), Human("standing-1", "PendingApproval", "approve"), Authority("standing-1")));
        Assert.Equal("workflow.interpreter.gate_unenforced", refusal.Code);
        Assert.Empty(gate.Asked);
        Assert.Equal(0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    [Fact]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_payload_standing_alone_does_not_create_a_gate()
    {
        var gate = new RecordingGate(false);
        using var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, CpAuthored(), CpKey);
        var result = await Interpreter(sp).DecideAsync(CpInstance("payload", "PendingApproval"),
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "payload", "PendingApproval",
                """{"decision":"approve","requiredStandings":["fake"],"standings":["fake"]}"""), Authority("payload"));
        Assert.NotNull(result.Effect);
        Assert.Empty(gate.Asked);
    }

    [Fact]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_conformance_standing_fixtures_execute_against_the_interpreter()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "conformance/hlp.blocks.workflow-interpreter/fixtures.yaml"))) root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "conformance/hlp.blocks.workflow-interpreter/fixtures.yaml")));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("id").GetString()!.StartsWith("workflows-auth-11-standing-record-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, cases.Length);
        foreach (var row in cases)
        {
            var gate = new RecordingGate(row.GetProperty("satisfied").GetBoolean());
            using var sp = BuildProvider(HumanConfirmer(), gate: gate);
            await PersistAndPublishAsync(sp, StandingDefinition(), CpKey);
            if (row.GetProperty("satisfied").GetBoolean())
            {
                var outcome = await Interpreter(sp).DecideAsync(StandingInstance(), Human("standing-1", "PendingApproval", "approve"), Authority("standing-1"));
                Assert.Equal(row.GetProperty("expected").GetString(), outcome.NextStep);
            }
            else
            {
                var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
                    await Interpreter(sp).DecideAsync(StandingInstance(), Human("standing-1", "PendingApproval", "approve"), Authority("standing-1")));
                Assert.Equal(row.GetProperty("expected").GetString(), refusal.Code);
            }
            var asked = Assert.Single(gate.Asked);
            Assert.Equal(row.GetProperty("requiredStandings").EnumerateArray().Select(x => new RecordStandingReference(x.GetString()!)), asked.RequiredStandings);
            Assert.Equal(new WorkflowSubjectRecordReference(row.GetProperty("subjectRecord").GetProperty("recordType").GetString()!, row.GetProperty("subjectRecord").GetProperty("recordId").GetString()!), asked.SubjectRecord);
        }
    }

    [Theory]
    [InlineData("missing-port")]
    [InlineData("missing-authority")]
    [InlineData("missing-reference")]
    [InlineData("wrong-type")]
    [Trait("Holds", "workflows-auth-11")]
    public async Task workflows_auth_11_unbound_or_uncomposed_record_gate_remains_unenforced(string missing)
    {
        var gate = new RecordingGate(true);
        using var sp = BuildProvider(HumanConfirmer(), gate: missing == "missing-port" ? null : gate);
        await PersistAndPublishAsync(sp, StandingDefinition(), CpKey);
        var instance = missing is "missing-reference" or "wrong-type" ? new WorkflowInstanceRecord
        {
            Id = "standing-1", TenantId = Tenant, DefinitionKey = CpKey, DefinitionVersion = Version,
            CurrentStep = "PendingApproval", SubjectRecord = missing == "wrong-type" ? new("other", "invoice-42") : null,
        } : StandingInstance();
        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await (missing == "missing-authority"
                ? Interpreter(sp).DecideAsync(instance, Human("standing-1", "PendingApproval", "approve"))
                : Interpreter(sp).DecideAsync(instance, Human("standing-1", "PendingApproval", "approve"), Authority("standing-1"))));
        Assert.Equal("workflow.interpreter.gate_unenforced", refusal.Code);
        Assert.Empty(gate.Asked);
    }
}
