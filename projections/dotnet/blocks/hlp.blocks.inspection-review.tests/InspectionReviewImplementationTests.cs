using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.MultiTenancy;
using Harborline.Kernel.WorkItems;
using Xunit;

#pragma warning disable CS1591

namespace Harborline.Blocks.InspectionReview.Tests;

public sealed class InspectionReviewImplementationTests
{
    [Fact]
    public async Task ReviewRequiredProjectionCreatesExactlyOneReviewAcrossReplay()
    {
        var harness = Harness.Create();

        var first = await harness.Sink.DeliverAsync(harness.Envelope(2));
        var replay = await harness.Sink.DeliverAsync(harness.Envelope(2));
        var reviews = await harness.Service.ListPendingAsync();

        Assert.Empty(first.Skips);
        Assert.Empty(replay.Skips);
        var review = Assert.Single(reviews);
        Assert.Equal(InspectionConditionOutcome.ReviewRequired, review.ConditionOutcome);
        Assert.Equal(InspectionReviewLifecycle.Pending, review.Lifecycle);
        Assert.Equal("asset-1", review.Subject.Reference);
        Assert.Equal(2, review.ConditionScore);
        Assert.Equal([InspectionReviewDecision.Approve, InspectionReviewDecision.Rework], review.AllowedDecisions);
        Assert.Single(await harness.Store.ReadEventsAsync("tenant-a", review.ReviewId));
        Assert.Single(await harness.Store.ReadAuditAsync("tenant-a", review.ReviewId));
        Assert.Single(await harness.Store.ReadOutboxAsync("tenant-a"));
    }

    [Fact]
    public async Task AcceptedConditionCompletesProjectionWithoutReview()
    {
        var harness = Harness.Create();
        var result = await harness.Sink.DeliverAsync(harness.Envelope(4));
        Assert.Empty(result.Skips);
        Assert.Empty(await harness.Service.ListPendingAsync());
        Assert.Empty(await harness.Store.ReadOutboxAsync("tenant-a"));
    }

    [Theory]
    [InlineData("{}", InspectionReviewCodes.ConditionMissing)]
    [InlineData("{\"conditionRating\":0}", InspectionReviewCodes.ConditionInvalid)]
    [InlineData("{\"conditionRating\":\"2\"}", InspectionReviewCodes.ConditionInvalid)]
    public async Task InvalidConditionProducesValueFreeSkip(string json, string code)
    {
        var harness = Harness.Create();
        var result = await harness.Sink.DeliverAsync(harness.Envelope(json));
        var skip = Assert.Single(result.Skips);
        Assert.Equal(code, skip.Reason);
        Assert.Null(skip.Target);
        Assert.Empty(await harness.Service.ListPendingAsync());
    }

    [Fact]
    public async Task MissingOrForeignSubjectProducesSameSkipAndNoWork()
    {
        var missing = Harness.Create(caseReference: "missing");
        var foreign = Harness.Create(caseReference: "foreign");
        foreign.Subjects.Upsert(new TenantId("tenant-b"), new InspectionSubject("foreign", "Foreign"));

        var missingResult = await missing.Sink.DeliverAsync(missing.Envelope(1));
        var foreignResult = await foreign.Sink.DeliverAsync(foreign.Envelope(1));

        Assert.Equal(InspectionReviewCodes.SubjectNotFound, Assert.Single(missingResult.Skips).Reason);
        Assert.Equal(InspectionReviewCodes.SubjectNotFound, Assert.Single(foreignResult.Skips).Reason);
        Assert.Empty(await missing.Store.ReadOutboxAsync("tenant-a"));
        Assert.Empty(await foreign.Store.ReadOutboxAsync("tenant-a"));
    }

    [Fact]
    public async Task ProjectionTenantMismatchRemainsPendingWithoutSubjectLookupOrMutation()
    {
        var harness = Harness.Create(activeTenant: "tenant-b");
        var error = await Assert.ThrowsAsync<InspectionReviewProjectionException>(
            async () => await harness.Sink.DeliverAsync(harness.Envelope(1)));
        Assert.Equal(InspectionReviewCodes.ProjectionScopeMismatch, error.Code);
        Assert.Empty(await harness.Store.ReadOutboxAsync("tenant-a"));
        Assert.Empty(await harness.Store.ReadOutboxAsync("tenant-b"));
    }

    [Fact]
    public async Task SubjectAndReviewQueriesAreTenantScopedAndServerAuthorized()
    {
        var allowed = Harness.Create();
        allowed.Subjects.Upsert(new TenantId("tenant-b"), new InspectionSubject("foreign", "Foreign"));
        await allowed.Sink.DeliverAsync(allowed.Envelope(1));
        Assert.Single(await allowed.Service.ListSubjectsAsync());
        Assert.Null(await allowed.Service.GetSubjectAsync("foreign"));
        Assert.Single(await allowed.Service.ListPendingAsync());

        var denied = allowed with
        {
            Service = new InspectionReviewService(
                allowed.Context,
                allowed.Party,
                new FixedAuthorizer(false),
                allowed.Subjects,
                allowed.Kernel),
        };
        Assert.Empty(await denied.Service.ListSubjectsAsync());
        Assert.Empty(await denied.Service.ListPendingAsync());
        Assert.Null(await denied.Service.GetSubjectAsync("asset-1"));
    }

    [Fact]
    public async Task ApproveUsesExpectedVersionAndFingerprintBoundReplay()
    {
        var harness = Harness.Create();
        await harness.Sink.DeliverAsync(harness.Envelope(1));
        var pending = Assert.Single(await harness.Service.ListPendingAsync());

        var stale = await harness.Service.DecideAsync(Decision(pending, 99, "approve-1", InspectionReviewDecision.Approve));
        var committed = await harness.Service.DecideAsync(Decision(pending, 1, "approve-1", InspectionReviewDecision.Approve));
        var replay = await harness.Service.DecideAsync(Decision(pending, 1, "approve-1", InspectionReviewDecision.Approve));
        var conflict = await harness.Service.DecideAsync(Decision(pending, 1, "approve-1", InspectionReviewDecision.Rework));

        Assert.Equal(InspectionReviewMutationDisposition.VersionConflict, stale.Disposition);
        Assert.Equal(InspectionReviewMutationDisposition.Committed, committed.Disposition);
        Assert.Equal(InspectionReviewLifecycle.Approved, committed.Review?.Lifecycle);
        Assert.Equal(InspectionReviewMutationDisposition.Replayed, replay.Disposition);
        Assert.Equal(committed.Review, replay.Review);
        Assert.Equal(InspectionReviewMutationDisposition.IdempotencyConflict, conflict.Disposition);
        Assert.Empty(await harness.Service.ListPendingAsync());
        Assert.Null(await harness.Service.GetAsync(pending.ReviewId));
        Assert.Equal(2, (await harness.Store.ReadEventsAsync("tenant-a", pending.ReviewId)).Count);
    }

    [Fact]
    public async Task ReworkIsClosedTerminalDecisionAndPreservesNoteInOutbox()
    {
        var harness = Harness.Create();
        await harness.Sink.DeliverAsync(harness.Envelope(2));
        var pending = Assert.Single(await harness.Service.ListPendingAsync());
        var result = await harness.Service.DecideAsync(Decision(pending, 1, "rework-1", InspectionReviewDecision.Rework, "replace seal"));

        Assert.Equal(InspectionReviewMutationDisposition.Committed, result.Disposition);
        Assert.Equal(InspectionReviewLifecycle.Rework, result.Review?.Lifecycle);
        Assert.Empty(result.Review?.AllowedDecisions ?? []);
        var outbox = await harness.Store.ReadOutboxAsync("tenant-a");
        Assert.Contains(outbox, row => row.PayloadJson.Contains("replace seal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServiceCannotTransitionNonInspectionWorkItem()
    {
        var harness = Harness.Create();
        await harness.Kernel.CreateAsync(new CreateWorkItemRequest
        {
            Id = "other",
            SubjectRef = "asset-1",
            DefinitionKey = "other-process",
            DefinitionVersion = "1",
            InitialStep = "review",
            AllowedOutcomes = [new("approve", "review", "done", WorkItemStatus.Completed)],
            IdempotencyKey = "other-create",
        });

        var result = await harness.Service.DecideAsync(new DecideInspectionReviewRequest
        {
            ReviewId = "other",
            ExpectedVersion = 1,
            Decision = InspectionReviewDecision.Approve,
            IdempotencyKey = "other-decide",
        });

        Assert.Equal(InspectionReviewMutationDisposition.NotFound, result.Disposition);
        Assert.Equal(1, (await harness.Kernel.GetAsync("other"))?.Version);
    }

    [Fact]
    public async Task BindingResolverIsExactByTenantFormAndVersion()
    {
        var resolver = new InMemoryInspectionReviewBindingResolver();
        resolver.Register(new TenantId("tenant-a"), Harness.Binding);
        Assert.NotNull(await resolver.ResolveAsync(new TenantId("tenant-a"), Harness.Binding.FormId, Harness.Binding.FormVersion));
        Assert.Null(await resolver.ResolveAsync(new TenantId("tenant-b"), Harness.Binding.FormId, Harness.Binding.FormVersion));
        Assert.Null(await resolver.ResolveAsync(new TenantId("tenant-a"), Harness.Binding.FormId, new SemanticVersion(2, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => resolver.Register(
            new TenantId("tenant-a"),
            Harness.Binding with { ReviewRequiredAtOrBelow = 3 }));
    }

    private static DecideInspectionReviewRequest Decision(
        InspectionReviewView review,
        long version,
        string key,
        InspectionReviewDecision decision,
        string? note = null) => new()
    {
        ReviewId = review.ReviewId,
        ExpectedVersion = version,
        IdempotencyKey = key,
        Decision = decision,
        Note = note,
    };

    private sealed record Harness(
        TestContext Context,
        TestPartyContext Party,
        InMemoryInspectionSubjectDirectory Subjects,
        InMemoryWorkItemStore Store,
        WorkItemKernel Kernel,
        InspectionReviewProjectionSink Sink,
        InspectionReviewService Service,
        string CaseReference)
    {
        internal static readonly InspectionReviewBinding Binding = new()
        {
            FormId = new FormDefinitionId("equipment-inspection.v1"),
            FormVersion = new SemanticVersion(1, 0, 0),
            ConditionPointer = "/conditionRating",
            ReviewRequiredAtOrBelow = 2,
        };

        internal static Harness Create(string activeTenant = "tenant-a", string caseReference = "asset-1")
        {
            var context = new TestContext(activeTenant);
            var party = new TestPartyContext();
            var subjects = new InMemoryInspectionSubjectDirectory();
            subjects.Upsert(new TenantId("tenant-a"), new InspectionSubject("asset-1", "Pump 1", "{\"kind\":\"pump\"}"));
            var bindings = new InMemoryInspectionReviewBindingResolver();
            bindings.Register(new TenantId("tenant-a"), Binding);
            var store = new InMemoryWorkItemStore();
            var kernel = new WorkItemKernel(context, party, store, new FixedTimeProvider());
            var sink = new InspectionReviewProjectionSink(context, party, bindings, subjects, kernel);
            var service = new InspectionReviewService(context, party, new FixedAuthorizer(true), subjects, kernel);
            return new(context, party, subjects, store, kernel, sink, service, caseReference);
        }

        internal FormProjectionEnvelope Envelope(int condition) => Envelope($"{{\"conditionRating\":{condition},\"status\":\"pass\"}}");

        internal FormProjectionEnvelope Envelope(string json) => new(
            OutboxId: "forms-outbox-1",
            InstanceId: new EntityId("harborline", "forms", "submission-1"),
            Tenant: new TenantId("tenant-a"),
            PartyId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ActorId: "user-a",
            FormId: Binding.FormId,
            DefinitionVersion: Binding.FormVersion,
            CaseReference: CaseReference,
            ProtectedAcceptedValues: Encoding.UTF8.GetBytes(json),
            SubmittedAt: new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
    }

    private sealed class TestContext(string tenant) : IAuthenticatedActorContext
    {
        public string UserId => "user-a";
        public IReadOnlyList<string> Roles => ["InspectionReviewer"];
        public TenantMetadata? Tenant { get; } = new()
        {
            Id = new TenantId(tenant),
            Name = tenant,
            Status = TenantStatus.Active,
        };
    }

    private sealed class TestPartyContext : IPartyContext
    {
        public ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    private sealed class FixedAuthorizer(bool allowed) : IInspectionReviewAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(InspectionReviewOperation operation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
