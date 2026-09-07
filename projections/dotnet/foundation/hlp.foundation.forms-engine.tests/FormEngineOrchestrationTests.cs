using System.Runtime.CompilerServices;
using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FormEngineOrchestrationTests
{
    private static readonly TenantId Tenant = new("tenant-engine");
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

    [Fact]
    public async Task Validate_resolves_one_current_scope_and_preserves_schema_codes()
    {
        var harness = await Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":""}""");
        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, row => row.Code.HasValue && row.Code.Value == "minLength");
        Assert.Equal([FormEngineAction.Validate], harness.Context.Actions);
    }

    [Fact]
    public async Task Validate_uses_current_roles_and_denies_unwritable_fields()
    {
        var harness = await Harness.CreateAsync(roles: ["reader"]);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);
        Assert.Contains(result.Errors, row => row.Kind == Harborline.Contracts.Forms.ValidationErrorKind.Authorization);
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
    }

    [Fact]
    public async Task SaveAsync_SubmissionAuditAndOutboxCommitAtomically()
    {
        var harness = await Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada","secret":"cleartext-pii"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "idem", "case-7"));
        Assert.Equal(FormProjectionStatus.Complete, receipt.ProjectionStatus);
        Assert.Equal(Now, receipt.SubmittedAt);
        var delivered = Assert.Single(harness.Projection.Delivered);
        Assert.Equal("case-7", delivered.CaseReference);
        Assert.DoesNotContain("cleartext-pii", System.Text.Encoding.UTF8.GetString(delivered.ProtectedAcceptedValues.Span));
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task ProjectionDelivery_FailureRemainsPendingAndRetries()
    {
        var state = new InMemoryFormSubmissionState();
        var failing = new RecordingProjection(failures: 1);
        var first = await Harness.CreateAsync(state: state, projection: failing);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await first.Engine.SubmitAsync(new(first.Definition.Id, candidate, "idem"));
        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);

        var restarted = await Harness.CreateAsync(state: state);
        var result = await restarted.Engine.RecoverProjectionsAsync(10);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), result);
        Assert.Equal((1, 1, 0, 1), await restarted.Store.CountsAsync());
    }

    [Fact]
    public async Task Submit_same_key_replays_and_changed_accepted_candidate_conflicts()
    {
        var harness = await Harness.CreateAsync();
        using var first = JsonDocument.Parse("""{"name":"Ada"}""");
        using var same = JsonDocument.Parse("""{"name":"Ada"}""");
        using var different = JsonDocument.Parse("""{"name":"Grace"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, first, "idem"));
        var replay = await harness.Engine.SubmitAsync(new(harness.Definition.Id, same, "idem"));
        Assert.Equal(receipt, replay);
        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, different, "idem")));
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task RenderAsync_DecryptGrantAuditFailure_NeverReturnsCleartext()
    {
        var security = new RecordingSecurity(JsonDocument.Parse("""{"name":"Ada","secret":"revealed"}"""));
        var harness = await Harness.CreateAsync(security: security, readAudit: new ThrowingReadAudit());
        using var submit = JsonDocument.Parse("""{"name":"Ada","secret":"cleartext-pii"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, submit, "idem"));
        var view = await harness.Engine.RenderAsync(harness.Definition.Id, receipt.InstanceId);
        var secret = view.Sections.Single().Fields.Single(row => row.Name == "secret");
        Assert.True(secret.IsSensitive);
        Assert.False(secret.IsReadable);
        Assert.False(secret.Value.HasValue);
    }

    [Fact]
    public async Task Render_missing_and_foreign_tenant_instances_are_indistinguishable()
    {
        var harness = await Harness.CreateAsync();
        var missing = new EntityId("harborline", "forms", "missing");
        var first = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () => await harness.Engine.RenderAsync(harness.Definition.Id, missing));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "foreign-instance"));
        var foreignHarness = await Harness.CreateAsync(scopeTenant: new TenantId("foreign"), state: harness.State);
        var second = await Assert.ThrowsAsync<FormEngineNotFoundException>(async () => await foreignHarness.Engine.RenderAsync(foreignHarness.Definition.Id, receipt.InstanceId));
        Assert.Equal(first.Code, second.Code);
        Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public async Task Validate_candidate_over_limit_fails_before_provider_mutation()
    {
        var harness = await Harness.CreateAsync(options: new FormEngineOptions { MaximumCandidateBytes = 16 });
        using var candidate = JsonDocument.Parse("""{"name":"this is too long"}""");
        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);
        Assert.Contains(result.Errors, row => row.Kind == Harborline.Contracts.Forms.ValidationErrorKind.ResourceBound);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    internal sealed class Harness
    {
        private Harness(FormDefinition definition, InMemoryFormSubmissionState state, InMemoryFormSubmissionStore store, RecordingContext context, RecordingProjection projection, FormEngine engine)
            => (Definition, State, Store, Context, Projection, Engine) = (definition, state, store, context, projection, engine);

        public FormDefinition Definition { get; }
        public InMemoryFormSubmissionState State { get; }
        public InMemoryFormSubmissionStore Store { get; }
        public RecordingContext Context { get; }
        public RecordingProjection Projection { get; }
        public FormEngine Engine { get; }

        public static async Task<Harness> CreateAsync(
            IReadOnlyList<string>? roles = null,
            TenantId? scopeTenant = null,
            InMemoryFormSubmissionState? state = null,
            RecordingProjection? projection = null,
            IFormFieldSecurity? security = null,
            IFormSensitiveReadAudit? readAudit = null,
            FormEngineOptions? options = null,
            string? schemaJson = null,
            Func<string, TenantId, FormDefinition>? definitionFactory = null,
            IReuseResolver? reuseResolver = null,
            Func<FormSubmissionCommit, Exception?>? commitFailure = null,
            IFormProjectionSink? projectionSink = null,
            ISchemaRegistry? schemaRegistry = null,
            Exception? contextFailure = null)
        {
            var schemas = schemaRegistry ?? new InMemorySchemaRegistry();
            var schema = await schemas.RegisterAsync(schemaJson ?? """{"type":"object","properties":{"name":{"type":"string","minLength":1},"secret":{"type":"string"}},"required":["name"],"additionalProperties":false}""");
            var definition = (definitionFactory ?? CreateDefinition)(schema.Id.Value, scopeTenant ?? Tenant);
            var actualState = state ?? new InMemoryFormSubmissionState();
            var store = new InMemoryFormSubmissionStore(actualState, commitFailure);
            var heldNames = roles ?? ["admin"];
            var heldReferences = heldNames
                .Where(name => name != FormEnginePermissions.DecryptSensitive)
                .Select(RoleReference.Domain).Distinct().ToArray();
            var vocabularyReferences = definition.Overlay.Sections
                .SelectMany(section => section.Access.ReadRoles.Concat(section.Access.WriteRoles))
                .Concat(definition.Overlay.Fields.Values.SelectMany(field =>
                    (field.FieldReadRoles ?? []).Concat(field.FieldWriteRoles ?? [])))
                .Concat(heldReferences)
                .Distinct()
                .ToArray();
            var roleVocabulary = RoleVocabulary.FromApi(vocabularyReferences.Select(role => new RoleDefinition(
                Guid.NewGuid(), role, role.Name, new(RoleOwnerKind.Package, "forms-engine-tests"), false)));
            var context = new RecordingContext(
                new(scopeTenant ?? Tenant, Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", heldNames,
                    roleVocabulary, new HeldRoleSet(heldReferences)), contextFailure);
            var recordingProjection = projection ?? new RecordingProjection();
            var sink = projectionSink ?? recordingProjection;
            var actualSecurity = security ?? new RecordingSecurity();
            var engine = new FormEngine(context, new DefinitionStore(definition), reuseResolver ?? new IdentityReuseResolver(), schemas, actualSecurity,
                readAudit ?? new RecordingReadAudit(), store, sink, options, new FixedClock(Now));
            return new(definition, actualState, store, context, recordingProjection, engine);
        }

        internal static FormDefinition CreateDefinition(string schemaId, TenantId tenant) => new(
            new("inspection"), new(1, 0, 0), FormDefinitionStatus.Published, tenant, IdentityRef.System, new(schemaId),
            new(
                new Dictionary<string, FieldOverlay>
                {
                    ["name"] = new(InternationalizedText.FromInvariant("Name")),
                    ["secret"] = new(InternationalizedText.FromInvariant("Secret"), PiiSensitivity: PiiSensitivity.Sensitive),
                },
                [new("main", InternationalizedText.FromInvariant("Main"), ["name", "secret"], new([Harborline.Contracts.Authorization.RoleReference.Domain("admin"), Harborline.Contracts.Authorization.RoleReference.Domain("reader")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]))],
                Array.Empty<RuleDefinition>()),
            null, Now, Now);
    }

    internal sealed class RecordingContext(FormExecutionScope scope, Exception? failure = null) : IFormExecutionContextProvider
    {
        public List<FormEngineAction> Actions { get; } = [];
        public ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            if (failure is not null) return ValueTask.FromException<FormExecutionScope>(failure);
            return ValueTask.FromResult(scope);
        }
    }

    private sealed class DefinitionStore(FormDefinition definition) : IFormDefinitionStore
    {
        public ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default) =>
            ValueTask.FromResult<FormDefinition?>(tenant == definition.Tenant && id == definition.Id ? definition : null);
        public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> CreateAsync(FormDefinition schema, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> DeprecateAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> WithdrawAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RestorePackProjectionAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<FormDefinition> ListByTenantAsync(TenantId tenant, [EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
        public async IAsyncEnumerable<FormDefinition> ListCurrentPublishedByTenantAsync(TenantId tenant, [EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
    }

    private sealed class IdentityReuseResolver : IReuseResolver
    {
        public ValueTask<ResolvedFormDefinition> ResolveAsync(FormDefinition definition, CancellationToken ct = default) =>
            ValueTask.FromResult(new ResolvedFormDefinition(definition, new Dictionary<string, ReuseProvenance>()));
    }

    internal sealed class RecordingSecurity : IFormFieldSecurity
    {
        private readonly JsonDocument? _read;
        public RecordingSecurity(JsonDocument? read = null) => _read = read;
        public ValueTask<FormProtectionResult> ProtectAsync(FormExecutionScope scope, FormDefinition definition, EntityId instanceId, JsonDocument acceptedCandidate, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProtectionResult("{\"protected\":true}"u8.ToArray(), new HashSet<string>(["secret"], StringComparer.Ordinal)));
        public ValueTask<FormReadableCandidate> ReadAsync(FormExecutionScope scope, FormDefinition definition, FormSubmissionRecord submission, CancellationToken cancellationToken = default)
        {
            var document = _read ?? JsonDocument.Parse("{}");
            var fields = document.RootElement.EnumerateObject().Select(property => new FormFieldReadDecision(
                property.Name,
                property.Name == "secret" ? FormFieldReadDisposition.DecryptGranted : FormFieldReadDisposition.Plaintext,
                property.Value.Clone(),
                property.Name == "secret",
                property.Name == "secret"
                    ? [new FormSensitiveReadAuditEvent(
                        "secret",
                        FormSensitiveReadAuditKind.DecryptOnRender,
                        FormSensitiveReadAuditOutcome.Granted,
                        FormEnginePermissions.DecryptSensitive,
                        FormEnginePermissions.DecryptOnRenderPurpose,
                        DecryptCapabilityId: "test-capability")]
                    : [])).ToArray();
            return ValueTask.FromResult(new FormReadableCandidate(fields));
        }
    }

    private sealed class RecordingReadAudit : IFormSensitiveReadAudit
    {
        public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ThrowingReadAudit : IFormSensitiveReadAudit
    {
        public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default) => ValueTask.FromException(new IOException("audit unavailable"));
    }

    internal sealed class RecordingProjection(int failures = 0, FormProjectionSkip[]? skips = null) : IFormProjectionSink
    {
        private int _remainingFailures = failures;
        public List<FormProjectionEnvelope> Delivered { get; } = [];
        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(FormProjectionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (_remainingFailures-- > 0) return ValueTask.FromException<FormProjectionDeliveryResult>(new IOException("projection unavailable"));
            Delivered.Add(envelope);
            return ValueTask.FromResult(new FormProjectionDeliveryResult(skips ?? []));
        }
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
