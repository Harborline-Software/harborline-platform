using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.InspectionReview;
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

var tenant = new TenantId("tenant-package");
var actor = new ActorContext(tenant);
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
var workItems = new WorkItemKernel(actor, party, new InMemoryWorkItemStore(), new FixedTimeProvider());
var sink = new InspectionReviewProjectionSink(actor, party, bindings, subjects, workItems);
var reviews = new InspectionReviewService(actor, party, new AllowReview(), subjects, workItems);

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

using var candidate = JsonDocument.Parse("""{"conditionRating":2,"status":"pass"}""");
var receipt = await engine.SubmitAsync(new(binding.FormId, candidate, "package-submit-1", "asset-1"));
if (receipt.ProjectionStatus != FormProjectionStatus.Complete)
    throw new InvalidOperationException("Inspection projection did not complete.");
var pending = (await reviews.ListPendingAsync()).Single();
if (pending.ConditionOutcome != InspectionConditionOutcome.ReviewRequired
    || pending.SubmissionId != receipt.InstanceId.ToString())
    throw new InvalidOperationException("Trusted condition projection did not create the expected review.");
var approved = await reviews.DecideAsync(new DecideInspectionReviewRequest
{
    ReviewId = pending.ReviewId,
    ExpectedVersion = pending.Version,
    Decision = InspectionReviewDecision.Approve,
    IdempotencyKey = "package-review-1",
    Note = "verified from package-only host",
});
if (approved.Disposition != InspectionReviewMutationDisposition.Committed
    || approved.Review?.Lifecycle != InspectionReviewLifecycle.Approved
    || (await reviews.ListPendingAsync()).Count != 0)
    throw new InvalidOperationException("Authenticated review decision did not complete.");

Console.WriteLine($"INSPECTION_REVIEW_PACKAGE_PASS:{receipt.InstanceId}:{approved.Review.ReviewId}");

static FormDefinition Definition(InspectionReviewBinding binding, TenantId tenant, string schema) => new(
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

sealed class FormContext(TenantId tenant) : IFormExecutionContextProvider
{
    private static readonly Harborline.Contracts.Authorization.RoleReference Inspector =
        Harborline.Contracts.Authorization.RoleReference.Domain("Inspector");
    private static readonly Harborline.Contracts.Authorization.RoleVocabulary Vocabulary =
        Harborline.Contracts.Authorization.RoleVocabulary.FromApi([new(
            Guid.Parse("8ef668e7-d80a-41db-a8ca-1db2368f643e"), Inspector, "Inspector",
            new(Harborline.Contracts.Authorization.RoleOwnerKind.Package, "inspection-review"), false)]);

    public ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FormExecutionScope(
            tenant,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            ["Inspector"], Vocabulary, new Harborline.Contracts.Authorization.HeldRoleSet([Inspector])));
}

sealed class DefinitionStore(FormDefinition definition) : IFormDefinitionStore
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

sealed class IdentityReuseResolver : IReuseResolver
{
    public ValueTask<ResolvedFormDefinition> ResolveAsync(FormDefinition definition, CancellationToken ct = default) =>
        ValueTask.FromResult(new ResolvedFormDefinition(definition, new Dictionary<string, ReuseProvenance>()));
}

sealed class PassThroughFieldSecurity : IFormFieldSecurity
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

sealed class NoOpReadAudit : IFormSensitiveReadAudit
{
    public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

sealed class ActorContext(TenantId tenant) : IAuthenticatedActorContext
{
    public string UserId => "user-a";
    public IReadOnlyList<string> Roles => ["InspectionReviewer"];
    public TenantMetadata? Tenant { get; } = new() { Id = tenant, Name = tenant.Value, Status = TenantStatus.Active };
}

sealed class PartyContext : IPartyContext
{
    public ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
}

sealed class AllowReview : IInspectionReviewAuthorizer
{
    public ValueTask<bool> IsAllowedAsync(InspectionReviewOperation operation, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);
}

sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
}
