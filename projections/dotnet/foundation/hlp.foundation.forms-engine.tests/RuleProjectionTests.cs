using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Security;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class RuleProjectionTests
{
    [Fact]
    public async Task RenderAsync_ProjectsVisibility_Required_And_Computed_ServerSide()
    {
        var (harness, receipt) = await BuildAsync("""{"trigger":"no","amount":5,"detail":"x","notes":""}""");

        var fields = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId)).Sections).Fields;

        var detail = Assert.Single(fields, field => field.Name == "detail");
        Assert.True(detail.Rules.HasValue);
        Assert.False(detail.Rules.Value!.Visible);
        Assert.True(detail.IsReadable);
        var notes = Assert.Single(fields, field => field.Name == "notes");
        Assert.True(notes.Rules.HasValue);
        Assert.False(notes.Rules.Value!.Required);
        var total = Assert.Single(fields, field => field.Name == "total");
        Assert.True(total.Rules.HasValue);
        Assert.True(total.Rules.Value!.Computed.HasValue);
        Assert.Equal(10, total.Rules.Value.Computed.Value.GetInt32());
    }

    [Fact]
    public async Task RenderAsync_ProjectsVisibleAndRequired_WhenRuleConditionHolds()
    {
        var (harness, receipt) = await BuildAsync("""{"trigger":"yes","amount":3,"detail":"x","notes":""}""");

        var fields = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId)).Sections).Fields;

        Assert.True(Assert.Single(fields, field => field.Name == "detail").Rules.Value!.Visible);
        Assert.True(Assert.Single(fields, field => field.Name == "notes").Rules.Value!.Required);
        Assert.Equal(6, Assert.Single(fields, field => field.Name == "total").Rules.Value!.Computed.Value.GetInt32());
    }

    [Fact]
    public async Task RenderAsync_RuleFreeField_HasNoRuleProjection()
    {
        var (harness, receipt) = await BuildAsync("""{"trigger":"no","amount":5}""");

        var fields = Assert.Single((await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId)).Sections).Fields;

        Assert.False(Assert.Single(fields, field => field.Name == "trigger").Rules.HasValue);
        Assert.False(Assert.Single(fields, field => field.Name == "amount").Rules.HasValue);
    }

    [Fact]
    public async Task RenderAsync_EvaluatesCompiledRuleWithTheEngineClock()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            schemaJson: """{"type":"object"}""",
            definitionFactory: ClockDefinition);

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);

        var notes = Assert.Single(Assert.Single(view.Sections).Fields);
        Assert.True(notes.Rules.HasValue);
        Assert.True(notes.Rules.Value!.Required);
    }

    [Fact]
    public async Task CandidateEvaluation_ValidatesTheMaterializedComputedValue()
    {
        var schemas = new Harborline.Kernel.SchemaValidation.InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync("""{"type":"object"}""");
        var baseDefinition = Definition(schema.Id.Value, new TenantId("tenant-engine"));
        var definition = baseDefinition with
        {
            Overlay = baseDefinition.Overlay with
            {
                Sections = baseDefinition.Overlay.Sections.Select(section => section with { Access = new([], []) }).ToArray(),
                Rules =
                [
                    new("cmp.total", State.RuleTier.JsonLogic, State.RuleScope.Field, "total", "{\"*\":[{\"var\":\"amount\"},2]}", State.RuleActionKind.Compute),
                    new("valid.total", State.RuleTier.JsonLogic, State.RuleScope.Schema, "", "{\"==\":[{\"var\":\"total\"},4]}", State.RuleActionKind.Validate),
                ],
            },
        };
        using var candidate = JsonDocument.Parse("""{"amount":2}""");
        using var evaluation = await FormCandidateEvaluator.EvaluateAsync(
            new(new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", ["reader"]),
            definition,
            candidate,
            schemas,
            FormEngineOptions.DefaultMaximumCandidateBytes,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Empty(evaluation.Errors);
        Assert.Equal(4, evaluation.AcceptedCandidate.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CandidateEvaluation_MaterializesAComputedValueBeforeItsPageGuardReadsIt()
    {
        var schemas = new Harborline.Kernel.SchemaValidation.InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync("""{"type":"object"}""");
        var baseDefinition = Definition(schema.Id.Value, new TenantId("tenant-engine"));
        var definition = baseDefinition with
        {
            Overlay = baseDefinition.Overlay with
            {
                Sections = baseDefinition.Overlay.Sections.Select(section => section with { Access = new([], []) }).ToArray(),
                Rules =
                [
                    new("cmp.total", State.RuleTier.JsonLogic, State.RuleScope.Field, "total", "{\"*\":[{\"var\":\"amount\"},2]}", State.RuleActionKind.Compute),
                ],
                Pages =
                [
                    new("computed-page", State.InternationalizedText.FromInvariant("Computed page"), ["main"], "{\"==\":[{\"var\":\"total\"},4]}"),
                ],
            },
        };
        using var candidate = JsonDocument.Parse("""{"amount":2}""");

        using var evaluation = await FormCandidateEvaluator.EvaluateAsync(
            new(new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", ["reader"]),
            definition,
            candidate,
            schemas,
            FormEngineOptions.DefaultMaximumCandidateBytes,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Empty(evaluation.Errors);
        Assert.Equal(4, evaluation.AcceptedCandidate.RootElement.GetProperty("total").GetInt32());
        Assert.DoesNotContain("total", evaluation.HiddenFields);
    }

    // T-687: an uncompilable page guard withholds its page instead of faulting the whole evaluation.
    [Fact]
    public async Task CandidateEvaluation_HidesAPageWhoseGuardDoesNotCompile()
    {
        var schemas = new Harborline.Kernel.SchemaValidation.InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync("""{"type":"object"}""");
        var baseDefinition = Definition(schema.Id.Value, new TenantId("tenant-engine"));
        var definition = baseDefinition with
        {
            Overlay = baseDefinition.Overlay with
            {
                Sections = baseDefinition.Overlay.Sections.Select(section => section with { Access = new([], []) }).ToArray(),
                Rules = [],
                Pages = [new("bad-page", State.InternationalizedText.FromInvariant("Bad page"), ["main"], "{\"frobnicate\":[1]}")],
            },
        };
        using var candidate = JsonDocument.Parse("""{"amount":2}""");

        using var evaluation = await FormCandidateEvaluator.EvaluateAsync(
            new(new TenantId("tenant-engine"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", ["reader"]),
            definition, candidate, schemas, FormEngineOptions.DefaultMaximumCandidateBytes, TimeProvider.System, CancellationToken.None);

        Assert.Contains("amount", evaluation.HiddenFields);
    }

    private static async Task<(FormEngineOrchestrationTests.Harness Harness, FormSubmitReceipt Receipt)> BuildAsync(string json)
    {
        var readable = JsonDocument.Parse(json);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            schemaJson: """{"type":"object"}""",
            security: new FormEngineOrchestrationTests.RecordingSecurity(readable),
            definitionFactory: Definition);
        using var candidate = JsonDocument.Parse("""{"trigger":"no","amount":1,"detail":"x","notes":"ok"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, Guid.NewGuid().ToString("N")));
        return (harness, receipt);
    }

    private static State.FormDefinition Definition(string schema, TenantId tenant)
    {
        var fields = new[] { "trigger", "amount", "total", "detail", "notes" }.ToDictionary(
            field => field,
            field => new State.FieldOverlay(State.InternationalizedText.FromInvariant(field)),
            StringComparer.Ordinal);
        var now = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        return new(
            new("ruleform"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schema),
            new(
                fields,
                [new("main", State.InternationalizedText.FromInvariant("Main"), fields.Keys.ToArray(), new([Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("reader")]))],
                [
                    new("vis.detail", State.RuleTier.JsonLogic, State.RuleScope.Field, "detail", "{\"==\":[{\"var\":\"trigger\"},\"yes\"]}", State.RuleActionKind.Visibility),
                    new("req.notes", State.RuleTier.JsonLogic, State.RuleScope.Field, "notes", "{\"==\":[{\"var\":\"trigger\"},\"yes\"]}", State.RuleActionKind.Required),
                    new("cmp.total", State.RuleTier.JsonLogic, State.RuleScope.Field, "total", "{\"*\":[{\"var\":\"amount\"},2]}", State.RuleActionKind.Compute),
                ]),
            null,
            now,
            now);
    }

    private static State.FormDefinition ClockDefinition(string schema, TenantId tenant)
    {
        var now = FormEngineOrchestrationTests.Now;
        return new(
            new("clock-rule-form"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schema),
            new(
                new Dictionary<string, State.FieldOverlay>
                {
                    ["notes"] = new(State.InternationalizedText.FromInvariant("Notes")),
                },
                [new(
                    "main",
                    State.InternationalizedText.FromInvariant("Main"),
                    ["notes"],
                    new(
                        [Harborline.Contracts.Authorization.RoleReference.Domain("reader")],
                        [Harborline.Contracts.Authorization.RoleReference.Domain("reader")]))],
                [new(
                    "req.notes.today",
                    State.RuleTier.JsonLogic,
                    State.RuleScope.Field,
                    "notes",
                    "{\"==\":[{\"date.today\":[]},\"2026-08-08\"]}",
                    State.RuleActionKind.Required)]),
            null,
            now,
            now);
    }
}
