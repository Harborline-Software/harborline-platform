using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Kernel.SchemaValidation;
using KernelSchemaId = Harborline.Kernel.SchemaValidation.SchemaId;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class MigrationHardeningTests
{
    private static readonly TenantId TenantA = new("tenant-a");
    private static readonly TenantId TenantB = new("tenant-b");

    [Fact]
    public async Task RenderAsync_UnknownInactiveAndCrossTenant_AreIndistinguishable()
    {
        var unknownHarness = await FormEngineOrchestrationTests.Harness.CreateAsync(scopeTenant: TenantA);
        var unknown = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () =>
            await unknownHarness.Engine.RenderAsync(new("unknown"), null));

        var inactiveHarness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            scopeTenant: TenantA,
            contextFailure: new FormExecutionTenantUnavailableException());
        var inactive = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () =>
            await inactiveHarness.Engine.RenderAsync(inactiveHarness.Definition.Id, null));

        var crossTenantHarness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            scopeTenant: TenantB,
            definitionFactory: (schema, _) =>
                FormEngineOrchestrationTests.Harness.CreateDefinition(schema, TenantA));
        var crossTenant = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () =>
            await crossTenantHarness.Engine.RenderAsync(crossTenantHarness.Definition.Id, null));

        Assert.Equal((unknown.Code, unknown.Message), (inactive.Code, inactive.Message));
        Assert.Equal((unknown.Code, unknown.Message), (crossTenant.Code, crossTenant.Message));
    }

    [Fact]
    public async Task RenderAsync_SectionOrFieldReadDenied_WithholdsValue()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        var owner = await FormEngineOrchestrationTests.Harness.CreateAsync(
            state: state,
            definitionFactory: ReadRestrictedDefinition,
            security: new FormEngineOrchestrationTests.RecordingSecurity(
                JsonDocument.Parse("""{"name":"Ada","secret":"classified"}""")));
        using var candidate = JsonDocument.Parse("""{"name":"Ada","secret":"classified"}""");
        var receipt = await owner.Engine.SubmitAsync(new(owner.Definition.Id, candidate, "read-denial"));

        var denied = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            state: state,
            definitionFactory: ReadRestrictedDefinition,
            security: new FormEngineOrchestrationTests.RecordingSecurity(
                JsonDocument.Parse("""{"name":"Ada","secret":"classified"}""")));
        var view = await denied.Engine.RenderAsync(denied.Definition.Id, receipt.InstanceId);

        var fields = view.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.Equal(2, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.False(field.IsReadable);
            Assert.False(field.Value.HasValue);
        });
    }

    [Fact]
    public async Task SaveAsync_SectionOrFieldWriteDenied_PersistsNothing()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(roles: ["reader"]);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var error = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "write-denial")));

        Assert.Contains(error.Errors, row => row.Kind == Harborline.Contracts.Forms.ValidationErrorKind.Authorization);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
        Assert.Empty(harness.Projection.Delivered);
    }

    [Fact]
    public async Task ValidateAsync_FormOrSchemaVersionMissing_ReturnsStableNotFound()
    {
        var formMissing = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var first = JsonDocument.Parse("""{"name":"Ada"}""");
        var missingForm = await formMissing.Engine.ValidateAsync(new("missing"), first);

        var schemaMissing = await FormEngineOrchestrationTests.Harness.CreateAsync(
            definitionFactory: (_, tenant) => FormEngineOrchestrationTests.Harness.CreateDefinition("schema:missing", tenant));
        using var second = JsonDocument.Parse("""{"name":"Ada"}""");
        var missingSchema = await schemaMissing.Engine.ValidateAsync(schemaMissing.Definition.Id, second);

        Assert.False(missingForm.IsValid);
        Assert.False(missingSchema.IsValid);
        Assert.Equal("form.engine.not-found", Assert.Single(missingForm.Errors).Code.Value);
        Assert.Equal("form.engine.not-found", Assert.Single(missingSchema.Errors).Code.Value);
        Assert.Equal(Harborline.Contracts.Forms.ValidationErrorKind.NotFound, Assert.Single(missingForm.Errors).Kind);
        Assert.Equal(Harborline.Contracts.Forms.ValidationErrorKind.NotFound, Assert.Single(missingSchema.Errors).Kind);
    }

    [Fact]
    public async Task ValidateAsync_SchemaProviderTimeout_ReturnsResourceBound()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaRegistry: new ControlledSchemaRegistry((_, _, _) =>
                ValueTask.FromException<SchemaValidationResult>(new TimeoutException("schema budget exhausted"))));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var error = await Assert.ThrowsAsync<FormEngineResourceBoundException>(async () =>
            await harness.Engine.ValidateAsync(harness.Definition.Id, candidate));

        Assert.Equal("form.engine.resource-bound", error.Code);
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public async Task ValidateAsync_RegistryResourceLimitCodes_ArePreserved()
    {
        var registryError = new SchemaValidationError(
            "/name",
            "Validation exceeded the pattern budget.",
            "pattern-timeout",
            new Dictionary<string, string> { ["budgetMs"] = "200" });
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaRegistry: new ControlledSchemaRegistry((_, _, _) =>
                ValueTask.FromResult(new SchemaValidationResult(false, [registryError]))));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        var error = Assert.Single(result.Errors);
        Assert.Equal("pattern-timeout", error.Code.Value);
        Assert.Equal("200", error.Params.Value!["budgetMs"]);
    }

    [Fact]
    public async Task SaveAsync_NonObjectCandidate_FailsBeforePersistence()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""["not","an","object"]""");

        var error = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "array")));

        Assert.Equal("type", Assert.Single(error.Errors).Code.Value);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task SaveAsync_UncompilableAdmittedRule_FailsClosed()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaJson: """{"type":"object"}""",
            definitionFactory: UncompilableDefinition);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var error = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "bad-rule")));

        Assert.NotEmpty(error.Errors);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
        Assert.Empty(harness.Projection.Delivered);
    }

    [Fact]
    public async Task SaveAsync_SchemaRuleEvaluatesThePrunedCandidateThatIsPersisted()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaJson: """{"type":"object","additionalProperties":true}""",
            definitionFactory: PrunedRuleCandidateDefinition);
        using var hidden = JsonDocument.Parse("""{"trigger":"hide","evidence":"approved"}""");

        var refusal = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, hidden, "hidden-evidence")));

        var ruleError = Assert.Single(refusal.Errors, row => row.Code.HasValue && row.Code.Value == "requires-evidence");
        Assert.Equal("/evidence", ruleError.JsonPointer);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());

        using var visible = JsonDocument.Parse("""{"trigger":"show","evidence":"approved"}""");
        await harness.Engine.SubmitAsync(new(harness.Definition.Id, visible, "visible-evidence"));

        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task SubmitAsync_EvaluatesAcceptedCandidateWhileTheRealSubmissionScopeIsActive()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        var schemas = new ControlledSchemaRegistry((_, _, _) =>
        {
            Assert.Equal(0, state.Gate.CurrentCount);
            return ValueTask.FromResult(new SchemaValidationResult(true, Array.Empty<SchemaValidationError>()));
        });
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            state: state,
            schemaJson: """{"type":"object"}""",
            schemaRegistry: schemas);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "scope-active"));

        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task SubmitAsync_CapturesItsClockOnceAfterOpeningTheRealSubmissionScope()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        var instant = DateTimeOffset.Parse("2027-02-03T04:05:06Z");
        var clock = new ScopeObservingClock(state, instant);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(state: state, clock: clock);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "scope-clock"));

        Assert.Equal(1, clock.Reads);
        Assert.Equal(instant, receipt.SubmittedAt);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task RenderAndSubmit_ShareOneSuppliedInstantAcrossPageGuardComputedValueReceiptAndAudit()
    {
        var instant = DateTimeOffset.Parse("2027-02-03T04:05:06Z");
        var clock = new CountingClock(instant);
        var state = new Persistence.InMemoryFormSubmissionState();
        using var readable = JsonDocument.Parse("""{"amount":1}""");
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["reader"],
            state: state,
            clock: clock,
            schemaJson: """{"type":"object","additionalProperties":true}""",
            security: new FormEngineOrchestrationTests.RecordingSecurity(readable),
            definitionFactory: ClockedPageDefinition);
        using var candidate = JsonDocument.Parse("""{"amount":1}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "clocked-page"));

        Assert.Equal(1, clock.Reads);
        Assert.Equal(instant, receipt.SubmittedAt);
        var commit = Assert.Single(state.Idempotency.Values);
        Assert.Equal(instant, commit.Submission.SubmittedAt);
        Assert.Equal(instant, commit.Audit.RecordedAt);
        Assert.Equal(instant, commit.Receipt.SubmittedAt);
        using (var audit = JsonDocument.Parse(commit.Audit.Payload))
        {
            Assert.Equal(instant, audit.RootElement.GetProperty("binding").GetProperty("submittedAt").GetDateTimeOffset());
            Assert.Contains("today", audit.RootElement.GetProperty("acceptedFields").EnumerateArray().Select(value => value.GetString()));
        }

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);

        Assert.Equal(2, clock.Reads);
        var today = Assert.Single(Assert.Single(view.Sections).Fields, field => field.Name == "today");
        Assert.True(today.Rules.HasValue);
        Assert.True(today.Rules.Value!.Computed.HasValue);
        Assert.Equal("2027-02-03", today.Rules.Value.Computed.Value.GetString());
    }

    [Fact]
    public async Task SubmitAsync_PreparationFailureReleasesTheScopeForASuccessfulRetry()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        var failing = await FormEngineOrchestrationTests.Harness.CreateAsync(
            state: state,
            schemaJson: """{"type":"object"}""",
            schemaRegistry: new ControlledSchemaRegistry((_, _, _) =>
                ValueTask.FromException<SchemaValidationResult>(new IOException("schema unavailable"))));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(async () =>
            await failing.Engine.SubmitAsync(new(failing.Definition.Id, candidate, "prepare-failure")));
        Assert.Equal((0, 0, 0, 0), await failing.Store.CountsAsync());

        var retry = await FormEngineOrchestrationTests.Harness.CreateAsync(state: state);
        await retry.Engine.SubmitAsync(new(retry.Definition.Id, candidate, "prepare-retry"));
        Assert.Equal((1, 1, 0, 1), await retry.Store.CountsAsync());
    }

    [Fact]
    public async Task SubmitAsync_CancelledPreparationReleasesTheScopeForASuccessfulRetry()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        using var cancelled = new CancellationTokenSource();
        var failing = await FormEngineOrchestrationTests.Harness.CreateAsync(
            state: state,
            schemaJson: """{"type":"object"}""",
            schemaRegistry: new ControlledSchemaRegistry((_, _, _) =>
            {
                cancelled.Cancel();
                return ValueTask.FromCanceled<SchemaValidationResult>(cancelled.Token);
            }));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await failing.Engine.SubmitAsync(new(failing.Definition.Id, candidate, "prepare-cancel"), cancelled.Token));
        Assert.Equal((0, 0, 0, 0), await failing.Store.CountsAsync());

        var retry = await FormEngineOrchestrationTests.Harness.CreateAsync(state: state);
        await retry.Engine.SubmitAsync(new(retry.Definition.Id, candidate, "prepare-cancel-retry"));
        Assert.Equal((1, 1, 0, 1), await retry.Store.CountsAsync());
    }

    [Fact]
    public async Task SubmitAsync_CancellationWhileWaitingForTheRealSubmissionScope_CompletesBeforeTheHolderReleases()
    {
        var state = new Persistence.InMemoryFormSubmissionState();
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(state: state);
        await using var heldScope = await harness.Store.BeginTransactionAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        using var cancelled = new CancellationTokenSource();
        var waitingSubmit = harness.Engine.SubmitAsync(
            new(harness.Definition.Id, candidate, "cancel-while-waiting"),
            cancelled.Token).AsTask();

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            cancelled.Cancel();
            using var completionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            var completed = await Task.WhenAny(waitingSubmit, Task.Delay(Timeout.InfiniteTimeSpan, completionTimeout.Token));

            Assert.Same(waitingSubmit, completed);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitingSubmit);
        }
        finally
        {
            await heldScope.RollbackAsync();
            if (!waitingSubmit.IsCompleted)
            {
                try { await waitingSubmit; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Fact]
    public async Task SubmitAsync_RuleLivenessCancellationInsideTheOpenedScope_FailsClosedAsInfrastructure()
    {
        var schemas = new ControlledSchemaRegistry((_, _, _) =>
            ValueTask.FromResult(new SchemaValidationResult(true, Array.Empty<SchemaValidationError>())));
        var state = new Persistence.InMemoryFormSubmissionState();
        using var rulesLiveness = new CancellationTokenSource();
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            state: state,
            schemaJson: """{"type":"object"}""",
            definitionFactory: RuleDefinition,
            schemaRegistry: schemas,
            clock: new ScopeCancellingClock(state, rulesLiveness));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var saveError = await Assert.ThrowsAsync<FormEngineResourceBoundException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "rule-timeout"), rulesLiveness.Token));

        Assert.Equal("form.engine.resource-bound", saveError.Code);
        Assert.Equal(0, schemas.ValidateCalls);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
        Assert.Empty(harness.Projection.Delivered);
    }

    [Fact]
    public async Task RenderAsync_RuleTimeout_FailsClosed()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaJson: """{"type":"object"}""",
            definitionFactory: RuleDefinition);
        using var timeout = new CancellationTokenSource();
        timeout.Cancel();

        var error = await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(async () =>
            await harness.Engine.RenderAsync(harness.Definition.Id, null, timeout.Token));

        Assert.Equal("form.engine.provider-unavailable", error.Code);
        Assert.Empty(harness.Projection.Delivered);
    }

    private static State.FormDefinition UncompilableDefinition(string schema, TenantId tenant)
    {
        var original = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return original with
        {
            Overlay = original.Overlay with
            {
                Rules =
                [
                    new("bad-rule", State.RuleTier.JsonLogic, State.RuleScope.Field, "name", "{not-json", State.RuleActionKind.Visibility),
                ],
            },
        };
    }

    private static State.FormDefinition ReadRestrictedDefinition(string schema, TenantId tenant)
    {
        var original = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return original with
        {
            Overlay = original.Overlay with
            {
                Fields = new Dictionary<string, State.FieldOverlay>(StringComparer.Ordinal)
                {
                    ["name"] = original.Overlay.Fields["name"] with { FieldReadRoles = [Harborline.Contracts.Authorization.RoleReference.Domain("admin")] },
                    ["secret"] = original.Overlay.Fields["secret"],
                },
                Sections =
                [
                    new("field-restricted", State.InternationalizedText.FromInvariant("Field restricted"), ["name"], new([Harborline.Contracts.Authorization.RoleReference.Domain("admin"), Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")])),
                    new("section-restricted", State.InternationalizedText.FromInvariant("Section restricted"), ["secret"], new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")])),
                ],
            },
        };
    }

    private static State.FormDefinition RuleDefinition(string schema, TenantId tenant)
    {
        var original = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return original with
        {
            Overlay = original.Overlay with
            {
                Rules =
                [
                    new("validate-name", State.RuleTier.JsonLogic, State.RuleScope.Field, "name", "{\"==\":[{\"var\":\"name\"},\"Ada\"]}", State.RuleActionKind.Validate),
                ],
            },
        };
    }

    private static State.FormDefinition PrunedRuleCandidateDefinition(string schema, TenantId tenant)
    {
        var original = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return original with
        {
            Overlay = new(
                new Dictionary<string, State.FieldOverlay>(StringComparer.Ordinal)
                {
                    ["trigger"] = new(State.InternationalizedText.FromInvariant("Trigger")),
                    ["evidence"] = new(State.InternationalizedText.FromInvariant("Evidence")),
                },
                [new("main", State.InternationalizedText.FromInvariant("Main"), ["trigger", "evidence"], new([], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]))],
                [
                    new("show-evidence", State.RuleTier.JsonLogic, State.RuleScope.Field, "evidence", "{\"==\":[{\"var\":\"trigger\"},\"show\"]}", State.RuleActionKind.Visibility),
                    new("requires-evidence", State.RuleTier.JsonLogic, State.RuleScope.Schema, "", "{\"==\":[{\"var\":\"evidence\"},\"approved\"]}", State.RuleActionKind.Validate),
                ]),
        };
    }

    private static State.FormDefinition ClockedPageDefinition(string schema, TenantId tenant)
    {
        var fields = new Dictionary<string, State.FieldOverlay>(StringComparer.Ordinal)
        {
            ["amount"] = new(State.InternationalizedText.FromInvariant("Amount")),
            ["today"] = new(State.InternationalizedText.FromInvariant("Today")),
        };
        var original = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return original with
        {
            Overlay = original.Overlay with
            {
                Fields = fields,
                Sections = [new("main", State.InternationalizedText.FromInvariant("Main"), fields.Keys.ToArray(), new([Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("reader")]))],
                Rules = [new("cmp.today", State.RuleTier.JsonLogic, State.RuleScope.Field, "today", "{\"date.today\":[]}", State.RuleActionKind.Compute)],
                Pages = [new("today-page", State.InternationalizedText.FromInvariant("Today"), ["main"], "{\"==\":[{\"var\":\"today\"},\"2027-02-03\"]}")],
            },
        };
    }

    private sealed class ControlledSchemaRegistry(
        Func<KernelSchemaId, ReadOnlyMemory<byte>, CancellationToken, ValueTask<SchemaValidationResult>> validate) : ISchemaRegistry
    {
        private readonly InMemorySchemaRegistry _inner = new();

        public int ValidateCalls { get; private set; }

        public ValueTask<Schema?> GetAsync(KernelSchemaId id, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(id, cancellationToken);

        public ValueTask<Schema> RegisterAsync(
            string jsonSchemaText,
            IReadOnlyList<KernelSchemaId>? parents = null,
            IReadOnlyList<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            _inner.RegisterAsync(jsonSchemaText, parents, tags, cancellationToken);

        public ValueTask<SchemaValidationResult> ValidateAsync(
            KernelSchemaId id,
            ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
        {
            ValidateCalls++;
            return validate(id, documentBytes, cancellationToken);
        }

        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(tagFilter, cancellationToken);
    }

    private sealed class ScopeObservingClock(Persistence.InMemoryFormSubmissionState state, DateTimeOffset instant) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Assert.Equal(0, state.Gate.CurrentCount);
            Reads++;
            return instant;
        }
    }

    private sealed class CountingClock(DateTimeOffset instant) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return instant;
        }
    }

    private sealed class ScopeCancellingClock(Persistence.InMemoryFormSubmissionState state, CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            Assert.Equal(0, state.Gate.CurrentCount);
            cancellation.Cancel();
            return FormEngineOrchestrationTests.Now;
        }
    }
}
