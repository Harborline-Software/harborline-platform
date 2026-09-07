using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Drafts;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

public sealed class FormsStateGapTests
{
    private static readonly TenantId Tenant = new("tenant-state-gaps");
    private static readonly FormDefinitionId FormOne = new("inspection");

    [Fact]
    public async Task CreateAsync_is_atomic_for_a_definition_id()
    {
        using var store = new InMemoryFormDefinitionStore();
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 24).Select(async _ =>
        {
            try
            {
                await store.CreateAsync(definition);
                return true;
            }
            catch (FormDefinitionConflictException)
            {
                return false;
            }
        }));

        Assert.Single(outcomes, created => created);
    }

    [Fact]
    public async Task RegisterAndPublishAsync_exposes_only_the_published_revision()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        using var store = new InMemoryFormDefinitionStore(clock);
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));

        var published = await store.RegisterAndPublishAsync(definition);

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal(clock.GetUtcNow(), published.UpdatedAt);
        Assert.Equal(published, await store.GetAsync(Tenant, FormOne, definition.Version));
        Assert.Equal(published, await store.GetCurrentPublishedAsync(Tenant, FormOne));
    }

    [Fact]
    public async Task RegisterAndPublishAsync_cancellation_persists_nothing()
    {
        using var store = new InMemoryFormDefinitionStore();
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RegisterAndPublishAsync(definition, cancellation.Token).AsTask());

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(Tenant, FormOne, definition.Version).AsTask());
    }

    [Fact]
    public async Task RegisterAndPublishAsync_cancellation_during_commit_window_persists_nothing()
    {
        using var cancellation = new CancellationTokenSource();
        using var store = new InMemoryFormDefinitionStore(
            new CancelingClock(cancellation, DateTimeOffset.Parse("2026-08-08T12:00:00Z")));
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RegisterAndPublishAsync(definition, cancellation.Token).AsTask());

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(Tenant, FormOne, definition.Version).AsTask());
    }

    [Fact]
    public async Task RegisterAndPublishAsync_identical_retry_returns_the_committed_revision()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
        using var store = new InMemoryFormDefinitionStore(clock);
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));

        var first = await store.RegisterAndPublishAsync(definition);
        var retry = await store.RegisterAndPublishAsync(definition);

        Assert.Equal(first, retry);
        Assert.Equal(first, await store.GetCurrentPublishedAsync(Tenant, FormOne));
    }

    [Fact]
    public async Task RegisterAndPublishAsync_different_retry_conflicts()
    {
        using var store = new InMemoryFormDefinitionStore();
        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0));
        await store.RegisterAndPublishAsync(definition);
        var different = definition with { SchemaRef = new SchemaId("sha256:different") };

        await Assert.ThrowsAsync<FormDefinitionConflictException>(
            () => store.RegisterAndPublishAsync(different).AsTask());
    }

    [Fact]
    public async Task Current_list_returns_one_highest_published_revision_per_id()
    {
        using var store = new InMemoryFormDefinitionStore();
        var v1 = Definition(FormOne, new SemanticVersion(1, 0, 0));
        var v2 = Definition(FormOne, new SemanticVersion(1, 0, 1));
        var draftOnly = Definition(new FormDefinitionId("draft-only"), new SemanticVersion(1, 0, 0));
        await store.RegisterAsync(v1);
        await store.PublishAsync(Tenant, FormOne, v1.Version);
        await store.RegisterAsync(v2);
        await store.PublishAsync(Tenant, FormOne, v2.Version);
        await store.RegisterAsync(draftOnly);

        var current = new List<FormDefinition>();
        await foreach (var definition in store.ListCurrentPublishedByTenantAsync(Tenant))
        {
            current.Add(definition);
        }

        var row = Assert.Single(current);
        Assert.Equal(FormOne, row.Id);
        Assert.Equal(v2.Version, row.Version);
    }

    [Fact]
    public async Task Draft_resume_and_abandon_are_bound_to_the_form_id()
    {
        var store = new InMemorySubmissionDraftStore();
        var actor = new FixedActorScope(new FormsActorScope(
            Tenant,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "alice"));
        var drafts = new SubmissionDraftService(actor, store);
        var caseId = DraftCaseId.NewId();
        await drafts.SaveDraftAsync(
            FormOne,
            caseId,
            new SubmissionDraftProvenance("schema", FormOne.Value, "1.0.0", SubmissionDraftProvenance.HarborlineJsonLogicV1, ["en-US"]),
            "{}"u8.ToArray());

        var other = new FormDefinitionId("other-form");
        Assert.Null(await drafts.ResumeDraftAsync(other, caseId));
        Assert.False(await drafts.AbandonDraftAsync(other, caseId));
        Assert.NotNull(await drafts.ResumeDraftAsync(FormOne, caseId));
        Assert.True(await drafts.AbandonDraftAsync(FormOne, caseId));
    }

    [Fact]
    public async Task Authoring_metadata_round_trips_exact_type_constraints_and_options()
    {
        using var store = new InMemoryFormDefinitionStore();
        var options = new List<string> { "PASS", "FAIL" };
        var validations = new List<FormFieldValidation>
        {
            new("required"),
            new("pattern", "^(PASS|FAIL)$"),
        };
        var authoring = new FormDefinitionAuthoring(new Dictionary<string, FormFieldAuthoringMetadata>
        {
            ["condition"] = new("radio", true, validations, options),
        });
        options.Add("MUTATED");
        validations.Clear();

        var definition = Definition(FormOne, new SemanticVersion(1, 0, 0), authoring);
        await store.RegisterAsync(definition);
        var loaded = await store.GetAsync(Tenant, FormOne, definition.Version);
        var field = Assert.Single(loaded.Authoring!.Fields).Value;

        Assert.Equal("radio", field.Type);
        Assert.Equal(["PASS", "FAIL"], field.Options);
        var loadedValidations = Assert.IsAssignableFrom<IReadOnlyList<FormFieldValidation>>(field.Validations);
        Assert.Equal(["required", "pattern"], loadedValidations.Select(item => item.Code));
        Assert.Equal("^(PASS|FAIL)$", loadedValidations[1].Param);
    }

    private static FormDefinition Definition(
        FormDefinitionId id,
        SemanticVersion version,
        FormDefinitionAuthoring? authoring = null) =>
        new(
            id,
            version,
            FormDefinitionStatus.Draft,
            Tenant,
            IdentityRef.System,
            new SchemaId($"sha256:{id.Value}-{version}"),
            new HarborlineOverlay(
                new Dictionary<string, FieldOverlay>
                {
                    ["condition"] = new(InternationalizedText.FromInvariant("Condition")),
                },
                [new FormSection(
                    "inspection",
                    InternationalizedText.FromInvariant("Inspection"),
                    ["condition"],
                    new SectionAccess([Harborline.Contracts.Authorization.RoleReference.Domain("inspector")], [Harborline.Contracts.Authorization.RoleReference.Domain("inspector")]))],
                Array.Empty<RuleDefinition>()),
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            authoring);

    private sealed class FixedActorScope(FormsActorScope scope) : IFormsActorScope
    {
        public ValueTask<FormsActorScope> GetRequiredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(scope);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CancelingClock(
        CancellationTokenSource cancellation,
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return now;
        }
    }
}
