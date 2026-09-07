using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.MultiTenancy;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.WorkItems;
using Xunit;

#pragma warning disable CS1591

namespace Harborline.Blocks.InspectionReview.Tests;

public sealed class InspectionReviewVerticalTests
{
    [Fact]
    public async Task RealFormsEngineSubmitCreatesAndCompletesAuthenticatedInspectionReview()
    {
        var tenant = new TenantId("tenant-a");
        var context = new ActorContext(tenant);
        var party = new PartyContext();
        var subjects = new InMemoryInspectionSubjectDirectory();
        subjects.Upsert(tenant, new InspectionSubject("asset-1", "Pump 1"));
        var bindings = new InMemoryInspectionReviewBindingResolver();
        var binding = new InspectionReviewBinding
        {
            FormId = new FormDefinitionId("equipment-inspection.v1"),
            FormVersion = new SemanticVersion(1, 0, 0),
            ConditionPointer = "/conditionRating",
            ReviewRequiredAtOrBelow = 2,
        };
        bindings.Register(tenant, binding);
        var workStore = new InMemoryWorkItemStore();
        var workItems = new WorkItemKernel(context, party, workStore, new FixedTimeProvider());
        var sink = new InspectionReviewProjectionSink(context, party, bindings, subjects, workItems);
        var reviews = new InspectionReviewService(context, party, new AllowReview(), subjects, workItems);

        var schemas = new InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync("""
            {"type":"object","properties":{"conditionRating":{"type":"integer","minimum":1,"maximum":5},"status":{"type":"string"}},"required":["conditionRating"],"additionalProperties":false}
            """);
        var definition = Definition(binding, tenant, schema.Id.Value);
        var engine = new FormEngine(
            new FormContext(tenant),
            new DefinitionStore(definition),
            new IdentityReuseResolver(),
            schemas,
            new PassThroughFieldSecurity(),
            new NoOpReadAudit(),
            new InMemoryFormSubmissionStore(),
            sink,
            clock: new FixedTimeProvider());

        using var firstCandidate = JsonDocument.Parse("""{"conditionRating":2,"status":"pass"}""");
        using var sameCandidate = JsonDocument.Parse("""{"status":"pass","conditionRating":2}""");
        using var changedCandidate = JsonDocument.Parse("""{"conditionRating":4,"status":"pass"}""");
        var receipt = await engine.SubmitAsync(new(binding.FormId, firstCandidate, "case-1", "asset-1"));
        var replay = await engine.SubmitAsync(new(binding.FormId, sameCandidate, "case-1", "asset-1"));
        await Assert.ThrowsAsync<FormEngineIdempotencyConflictException>(async () =>
            await engine.SubmitAsync(new(binding.FormId, changedCandidate, "case-1", "asset-1")));

        Assert.Equal(FormProjectionStatus.Complete, receipt.ProjectionStatus);
        Assert.Equal(receipt, replay);
        var pending = Assert.Single(await reviews.ListPendingAsync());
        Assert.Equal(receipt.InstanceId.ToString(), pending.SubmissionId);
        Assert.Equal(InspectionConditionOutcome.ReviewRequired, pending.ConditionOutcome);
        var approved = await reviews.DecideAsync(new DecideInspectionReviewRequest
        {
            ReviewId = pending.ReviewId,
            ExpectedVersion = pending.Version,
            Decision = InspectionReviewDecision.Approve,
            IdempotencyKey = "review-case-1",
            Note = "verified on site",
        });
        Assert.Equal(InspectionReviewMutationDisposition.Committed, approved.Disposition);
        Assert.Equal(InspectionReviewLifecycle.Approved, approved.Review?.Lifecycle);
        Assert.Empty(await reviews.ListPendingAsync());
    }

    private static FormDefinition Definition(InspectionReviewBinding binding, TenantId tenant, string schema) => new(
        binding.FormId,
        binding.FormVersion,
        FormDefinitionStatus.Published,
        tenant,
        IdentityRef.System,
        new Harborline.Foundation.Assets.Common.SchemaId(schema),
        new HarborlineOverlay(
            new Dictionary<string, FieldOverlay>
            {
                ["conditionRating"] = new(InternationalizedText.FromInvariant("Condition rating")),
                ["status"] = new(InternationalizedText.FromInvariant("Status")),
            },
            [new FormSection("inspection", InternationalizedText.FromInvariant("Inspection"), ["conditionRating", "status"], new SectionAccess([Harborline.Contracts.Authorization.RoleReference.Domain("Inspector")], [Harborline.Contracts.Authorization.RoleReference.Domain("Inspector")]))],
            Array.Empty<RuleDefinition>()),
        null,
        new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));

    private sealed class FormContext(TenantId tenant) : IFormExecutionContextProvider
    {
        public ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default)
        {
            var inspector = RoleReference.Domain("Inspector");
            var vocabulary = RoleVocabulary.FromApi([new(
                Guid.Parse("98a30c43-56bb-4741-8ca2-5e9f70c90ff0"), inspector, "Inspector",
                new(RoleOwnerKind.Package, "inspection-review-tests"), false)]);
            return ValueTask.FromResult(new FormExecutionScope(
                tenant,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "user-a",
                ["Inspector"],
                vocabulary,
                new HeldRoleSet([inspector])));
        }
    }

    private sealed class DefinitionStore(FormDefinition definition) : IFormDefinitionStore
    {
        public ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default) =>
            ValueTask.FromResult<FormDefinition?>(tenant == definition.Tenant && id == definition.Id ? definition : null);
        public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAsync(FormDefinition value, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition value, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> CreateAsync(FormDefinition value, CancellationToken ct = default) => throw new NotSupportedException();
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

    private sealed class PassThroughFieldSecurity : IFormFieldSecurity
    {
        public ValueTask<FormProtectionResult> ProtectAsync(
            FormExecutionScope scope,
            FormDefinition definition,
            EntityId instanceId,
            JsonDocument acceptedCandidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProtectionResult(
                Encoding.UTF8.GetBytes(acceptedCandidate.RootElement.GetRawText()),
                new HashSet<string>(StringComparer.Ordinal)));

        public ValueTask<FormReadableCandidate> ReadAsync(
            FormExecutionScope scope,
            FormDefinition definition,
            FormSubmissionRecord submission,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpReadAudit : IFormSensitiveReadAudit
    {
        public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ActorContext(TenantId tenant) : IAuthenticatedActorContext
    {
        public string UserId => "user-a";
        public IReadOnlyList<string> Roles => ["InspectionReviewer"];
        public TenantMetadata? Tenant { get; } = new() { Id = tenant, Name = tenant.Value, Status = TenantStatus.Active };
    }

    private sealed class PartyContext : IPartyContext
    {
        public ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    private sealed class AllowReview : IInspectionReviewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(InspectionReviewOperation operation, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
