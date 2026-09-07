using System.Text.Json;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class ProjectingFormEngineTests
{
    [Fact]
    public async Task Save_runs_the_projection_after_the_inner_save_with_the_submission_context()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "projection-context", "case-42"));

        var projection = Assert.Single(harness.Projection.Delivered);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
        Assert.Equal(receipt.InstanceId, projection.InstanceId);
        Assert.Equal(harness.Definition.Tenant, projection.Tenant);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), projection.PartyId);
        Assert.Equal("alice", projection.ActorId);
        Assert.Equal(harness.Definition.Id, projection.FormId);
        Assert.Equal(harness.Definition.Version, projection.DefinitionVersion);
        Assert.Equal(receipt.SubmittedAt, projection.SubmittedAt);
        Assert.NotEmpty(projection.ProtectedAcceptedValues.ToArray());
    }

    [Fact]
    public async Task Save_threads_the_case_ref_into_the_projection_context_and_the_inner_engine()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "case-present", "entity-42"));

        Assert.Equal("entity-42", Assert.Single(harness.Projection.Delivered).CaseReference);
        Assert.Equal("entity-42", Assert.Single(harness.State.Idempotency.Values).Projection.CaseReference);
    }

    [Fact]
    public async Task Save_without_a_case_ref_leaves_the_projection_context_case_null()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "case-absent"));

        Assert.Null(Assert.Single(harness.Projection.Delivered).CaseReference);
        Assert.Null(Assert.Single(harness.State.Idempotency.Values).Projection.CaseReference);
    }

    [Fact]
    public async Task Save_returns_the_engine_submit_instant_on_the_receipt()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "engine-clock"));

        Assert.Equal(FormEngineOrchestrationTests.Now, receipt.SubmittedAt);
    }

    [Fact]
    public async Task A_failed_inner_save_never_runs_the_projection()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            commitFailure: _ => new IOException("atomic commit unavailable"));
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        await Assert.ThrowsAsync<FormEngineProviderUnavailableException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "failed-submit")));

        Assert.Empty(harness.Projection.Delivered);
        Assert.Equal((0, 0, 0, 0), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task A_projection_reported_skip_rides_the_success_receipt()
    {
        var skip = new FormProjectionSkip("grade-out-of-range", "/condition", "wh-1");
        var projection = new FormEngineOrchestrationTests.RecordingProjection(skips: [skip]);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(projection: projection);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "projection-skip"));

        Assert.Equal(FormProjectionStatus.Complete, receipt.ProjectionStatus);
        Assert.Equal([skip], receipt.ProjectionSkips);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    [Fact]
    public async Task A_clean_save_reports_no_skips_on_the_receipt()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "projection-clean"));

        Assert.Equal(FormProjectionStatus.Complete, receipt.ProjectionStatus);
        Assert.Empty(receipt.ProjectionSkips);
    }

    [Fact]
    public async Task A_post_commit_projection_failure_surfaces_the_committed_receipt_as_pending()
    {
        var projection = new FormEngineOrchestrationTests.RecordingProjection(failures: 1);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(projection: projection);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "projection-pending"));

        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);
        Assert.Equal((1, 1, 1, 0), await harness.Store.CountsAsync());
        var pending = Assert.Single(harness.State.Pending.Values);
        Assert.Equal(receipt.InstanceId, pending.InstanceId);
        Assert.Equal(1, pending.Attempts);
        Assert.Equal("form.engine.projection-pending", pending.LastErrorCode);
    }

    [Fact]
    public async Task SaveAsync_ReceiptAuditAndOutboxShareSubmitInstant()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "shared-instant"));

        var commit = Assert.Single(harness.State.Idempotency.Values);
        Assert.Equal(receipt.SubmittedAt, commit.Submission.SubmittedAt);
        Assert.Equal(receipt.SubmittedAt, commit.Audit.RecordedAt);
        Assert.Equal(receipt.SubmittedAt, commit.Projection.SubmittedAt);
    }

    [Fact]
    public async Task ProjectionDelivery_CaseReferenceRoundTripsIncludingNull()
    {
        var present = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var first = JsonDocument.Parse("""{"name":"Ada"}""");
        await present.Engine.SubmitAsync(new(present.Definition.Id, first, "present", "case-7"));

        var absent = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var second = JsonDocument.Parse("""{"name":"Grace"}""");
        await absent.Engine.SubmitAsync(new(absent.Definition.Id, second, "absent"));

        Assert.Equal("case-7", Assert.Single(present.Projection.Delivered).CaseReference);
        Assert.Null(Assert.Single(absent.Projection.Delivered).CaseReference);
    }

    [Fact]
    public async Task ProjectionDelivery_SkipAndCleanOutcomesRemainObservable()
    {
        var skipping = await FormEngineOrchestrationTests.Harness.CreateAsync(
            projection: new FormEngineOrchestrationTests.RecordingProjection(
                skips: [new("capture-skipped", "/condition")]));
        using var first = JsonDocument.Parse("""{"name":"Ada"}""");
        var skipped = await skipping.Engine.SubmitAsync(new(skipping.Definition.Id, first, "skipped"));

        var clean = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var second = JsonDocument.Parse("""{"name":"Grace"}""");
        var completed = await clean.Engine.SubmitAsync(new(clean.Definition.Id, second, "clean"));

        Assert.Equal([new FormProjectionSkip("capture-skipped", "/condition")], skipped.ProjectionSkips);
        Assert.Empty(completed.ProjectionSkips);
    }

    [Fact]
    public async Task FailedSubmit_CreatesNoProjectionOutboxRow()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync();
        using var candidate = JsonDocument.Parse("""{"name":""}""");

        await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "no-outbox")));

        Assert.Empty(harness.State.Pending);
        Assert.Empty(harness.State.Idempotency);
        Assert.Empty(harness.Projection.Delivered);
    }

    [Fact]
    public async Task SaveAsync_OutboxReceivesAcceptedPrunedValues_NotOriginalCandidate() =>
        await AssertAcceptedProjectionAsync();

    [Fact]
    public async Task SaveAsync_ComputedAndPrunedValues_AreTheAcceptedCandidate() =>
        await AssertAcceptedProjectionAsync();

    [Fact]
    public async Task ProjectionDelivery_DuplicateAttemptProducesOneEffect()
    {
        var projection = new IdempotentEffectThenFaultProjection();
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(projectionSink: projection);
        using var candidate = JsonDocument.Parse("""{"name":"Ada"}""");

        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "duplicate-delivery"));
        Assert.Equal(FormProjectionStatus.Pending, receipt.ProjectionStatus);

        var recovery = await harness.Engine.RecoverProjectionsAsync(10);

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovery);
        Assert.Equal(2, projection.Attempts);
        Assert.Equal(1, projection.Effects);
        Assert.Equal((1, 1, 0, 1), await harness.Store.CountsAsync());
    }

    private static async Task AssertAcceptedProjectionAsync()
    {
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            schemaJson: """{"type":"object"}""",
            security: new AcceptedCandidateSecurity(),
            definitionFactory: ProjectionRuleDefinition);
        using var candidate = JsonDocument.Parse("""{"trigger":"no","amount":5,"detail":"must-prune"}""");

        await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, Guid.NewGuid().ToString("N")));

        using var accepted = JsonDocument.Parse(Assert.Single(harness.Projection.Delivered).ProtectedAcceptedValues);
        Assert.Equal("no", accepted.RootElement.GetProperty("trigger").GetString());
        Assert.Equal(5, accepted.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal(10, accepted.RootElement.GetProperty("total").GetInt32());
        Assert.False(accepted.RootElement.TryGetProperty("detail", out _));
    }

    private static State.FormDefinition ProjectionRuleDefinition(string schema, TenantId tenant)
    {
        var fields = new[] { "trigger", "amount", "total", "detail" }.ToDictionary(
            field => field,
            field => new State.FieldOverlay(State.InternationalizedText.FromInvariant(field)),
            StringComparer.Ordinal);
        return new(
            new("projection-rules"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Published,
            tenant,
            State.IdentityRef.System,
            new(schema),
            new(
                fields,
                [new("main", State.InternationalizedText.FromInvariant("Main"), fields.Keys.ToArray(), new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]))],
                [
                    new("vis.detail", State.RuleTier.JsonLogic, State.RuleScope.Field, "detail", "{\"==\":[{\"var\":\"trigger\"},\"yes\"]}", State.RuleActionKind.Visibility),
                    new("cmp.total", State.RuleTier.JsonLogic, State.RuleScope.Field, "total", "{\"*\":[{\"var\":\"amount\"},2]}", State.RuleActionKind.Compute),
                ]),
            null,
            FormEngineOrchestrationTests.Now,
            FormEngineOrchestrationTests.Now);
    }

    private sealed class AcceptedCandidateSecurity : IFormFieldSecurity
    {
        public ValueTask<FormProtectionResult> ProtectAsync(
            FormExecutionScope scope,
            State.FormDefinition definition,
            EntityId instanceId,
            JsonDocument acceptedCandidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new FormProtectionResult(
                Encoding.UTF8.GetBytes(acceptedCandidate.RootElement.GetRawText()),
                new HashSet<string>(StringComparer.Ordinal)));

        public ValueTask<FormReadableCandidate> ReadAsync(
            FormExecutionScope scope,
            State.FormDefinition definition,
            FormSubmissionRecord submission,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class IdempotentEffectThenFaultProjection : IFormProjectionSink
    {
        private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

        public int Attempts { get; private set; }
        public int Effects { get; private set; }

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(
            FormProjectionEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (_applied.Add(envelope.OutboxId)) Effects++;
            if (Attempts == 1)
                return ValueTask.FromException<FormProjectionDeliveryResult>(new IOException("completion acknowledgement lost"));
            return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
        }
    }
}
