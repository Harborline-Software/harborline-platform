using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

// Oracle: DES-0019 workflows-ck-20 to ck-22 and ADR-0057.
public sealed class WorkflowStateDeclarationsTests
{
    [Fact(DisplayName = "workflows-ck-20 workflows-ck-21 workflows-ck-22 workflows-auth-13 workflows-auth-14 workflows-auth-19: mapper retains state declarations and action notification")]
    public void Mapper_retains_state_declarations_and_action_notification()
    {
        using var json = JsonDocument.Parse("""
        {"initialState":"Open", "states":[{"id":"Open", "kind":"Normal", "fieldRequirements":[{"field":"invoice.amount","condition":"required.amount"}], "locks":"dates only", "escalation":{"schedule":"review-overdue"}}], "triggers":[], "transitions":[], "actions":[{"id":"notify", "on":{"state":"Open"}, "kind":"Notify", "capabilityRef":"notify.email", "classification":"AP", "notification":{"eventKey":"invoice.issued", "batchingWindow":"PT10M", "tier":"observation"}}], "guards":[]}
        """);
        var model = WorkflowDefinitionWireMapper.ToModel(json.RootElement, "tenant:acme", "invoice", "1.0.0");
        var state = Assert.Single(model.States);
        Assert.Equal("invoice.amount", Assert.Single(state.FieldRequirements).Field);
        Assert.Equal("required.amount", Assert.Single(state.FieldRequirements).Condition);
        Assert.Equal(Harborline.Contracts.Workflow.WorkflowLockScope.DatesOnly, state.Locks);
        Assert.IsType<Harborline.Contracts.Workflow.WorkflowLockScope>(state.Locks);
        Assert.Equal("review-overdue", state.Escalation!.Schedule);
        var notification = Assert.Single(model.Actions).Notification!;
        Assert.Equal("invoice.issued", notification.EventKey);
        Assert.Equal("PT10M", notification.BatchingWindow);
        Assert.Equal(Harborline.Contracts.Workflow.WorkflowNotificationTier.Observation, notification.Tier);
        Assert.IsType<Harborline.Contracts.Workflow.WorkflowNotificationTier>(notification.Tier);
    }
    [Fact(DisplayName = "workflows-ck-22: directly constructed invalid notifications are refused")]
    public void Admission_refuses_invalid_notification()
    {
        var definition = new WorkflowDefinition
        {
            Key = "invoice", Version = "1.0.0", Tenant = "tenant:acme", InitialState = "Done",
            States = [new WorkflowStateDef { Id = "Done", Kind = WorkflowStateKind.Terminal }],
            Actions = [new WorkflowActionBindingDef { Id = "notify", OnState = "Done", Kind = WorkflowActionKind.Notify, CapabilityRef = "notify.email", Classification = ActionClassification.AP, Notification = new WorkflowNotificationDef("", "PT10M", Harborline.Contracts.Workflow.WorkflowNotificationTier.Decision) }],
        };
        var result = new WorkflowAdmissionValidator().Validate(definition);
        Assert.Contains(result.Violations, violation => violation.Code == "workflow.invalid_notification");
    }
    [Fact(DisplayName = "workflows-ck-22: the engine mapper refuses notification on a state")]
    public void Mapper_refuses_notification_on_state()
    {
        using var json = JsonDocument.Parse("""
        {"initialState":"Open", "states":[{"id":"Open", "kind":"Normal", "notification":{}}], "triggers":[], "transitions":[], "actions":[], "guards":[]}
        """);
        var error = Assert.Throws<WorkflowGateReferenceException>(() => WorkflowDefinitionWireMapper.ToModel(json.RootElement, "t", "k", "1.0.0"));
        Assert.Equal("workflow.notification_on_state", error.Code);
    }
    [Theory(DisplayName = "workflows-ck-20 and workflows-ck-21: malformed state declarations are refused by the engine mapper")]
    [InlineData("\"fieldRequirements\":false", "invalid-array")]
    [InlineData("\"locks\":\"invalid\"", "workflow.invalid_field_lock")]
    [InlineData("\"escalation\":{\"schedule\":\"\"}", "workflow.invalid_escalation")]
    public void Mapper_refuses_malformed_state_declarations(string member, string code)
    {
        using var json = JsonDocument.Parse("{\"initialState\":\"Open\",\"states\":[{\"id\":\"Open\",\"kind\":\"Normal\"," + member + "}],\"triggers\":[],\"transitions\":[],\"actions\":[],\"guards\":[]}");
        var error = Assert.Throws<WorkflowGateReferenceException>(() => WorkflowDefinitionWireMapper.ToModel(json.RootElement, "t", "k", "1.0.0"));
        Assert.Equal(code, error.Code);
    }
}
