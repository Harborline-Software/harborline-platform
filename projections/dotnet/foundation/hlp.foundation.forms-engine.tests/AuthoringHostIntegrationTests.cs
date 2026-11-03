using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class AuthoringHostIntegrationTests
{
    [Fact]
    public async Task Actual_registered_publisher_exposes_prepublication_assessment_and_committed_form()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(fixture.Store);
        services.AddSingleton<Harborline.Kernel.SchemaValidation.ISchemaRegistry>(fixture.Schemas);
        services.AddSingleton<IFormPublishedSurfaceSource>(fixture.Context.Surfaces);
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var result = await publisher.RegisterAndPublishWithImpactAsync(fixture.Candidate,
            scope.ServiceProvider.FormPublicationContext(fixture.Basis));
        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Assessment.Compatibility);
        Assert.Equal("f-new-y8", result.Assessment.Proposal!.Candidate.VersionId);
        Assert.Equal("2.2.0", (await fixture.Store.GetCurrentPublishedAsync(fixture.Candidate.Tenant, fixture.Candidate.Id))!.Version.ToString());
        Assert.Equal(0, JsonNode.Parse(result.Assessment.Inputs!.CandidateFormJson)!["Status"]!.GetValue<int>());
        Assert.Equal(1, JsonNode.Parse(result.Assessment.Inputs.PredecessorFormJson!)!["Status"]!.GetValue<int>());
    }

    [Fact]
    public async Task First_publication_retains_the_assessed_Draft_separately_from_the_commit()
    {
        var fixture = await ImpactFixture.CreateAsync();
        fixture = fixture with { Store = new InMemoryFormDefinitionStore(), Basis = fixture.Basis with
            { NoPredecessor = true, Predecessor = null, PredecessorEnvironment = null } };
        await using var provider = BuildImpactHost(fixture);
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var result = await publisher.RegisterAndPublishWithImpactAsync(fixture.Candidate,
            scope.ServiceProvider.FormPublicationContext(fixture.Basis) with { Surfaces = new ImpactFixture.Source(_ =>
                ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-a", []))) });
        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, result.Assessment.Compatibility);
        Assert.Null(result.Assessment.Proposal);
        Assert.Empty(result.Assessment.Surfaces);
        var inputs = Assert.IsType<FormPublicationAssessedInputs>(result.Assessment.Inputs);
        var body = JsonNode.Parse(inputs.CandidateFormJson)!;
        Assert.Equal("2.2.0", body["Version"]!.GetValue<string>());
        Assert.Equal(0, body["Status"]!.GetValue<int>());
        Assert.Equal(DateTimeOffset.UnixEpoch, body["UpdatedAt"]!.GetValue<DateTimeOffset>());
        Assert.NotEqual(DateTimeOffset.UnixEpoch, result.Definition.UpdatedAt);
        Assert.Null(inputs.PredecessorFormJson);
        Assert.Null(inputs.PredecessorSchema);
        Assert.Equal("2.2.0", (await fixture.Store.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    [Fact]
    public async Task Competing_publishers_keep_historical_inputs_and_existing_identical_and_conflicting_retry_rules()
    {
        var fixture = await ImpactFixture.CreateAsync();
        await using var provider = BuildImpactHost(fixture);
        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();
        var publisherA = scopeA.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var publisherB = scopeB.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new ImpactFixture.Source(async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new FormPublishedSurfaceRead.Complete("tenant-a", []);
        });
        var contextA = scopeA.ServiceProvider.FormPublicationContext(fixture.Basis);
        var pendingA = publisherA.RegisterAndPublishWithImpactAsync(fixture.Candidate, contextA with { Surfaces = paused }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var candidateB = fixture.Candidate with { Version = new(2, 3, 0) };
        var basisB = fixture.Basis with { Candidate = fixture.Basis.Candidate with { Version = new(2, 3, 0), VersionId = "f-competing-z7" } };
        var resultB = await publisherB.RegisterAndPublishWithImpactAsync(candidateB, scopeB.ServiceProvider.FormPublicationContext(basisB));
        Assert.Equal(FormDefinitionStatus.Published, resultB.Definition.Status);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, resultB.Assessment.Compatibility);
        release.SetResult();
        var resultA = await pendingA.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(FormDefinitionStatus.Published, resultA.Definition.Status);
        Assert.Equal(FormPublicationCompatibility.ProposalEligible, resultA.Assessment.Compatibility);
        Assert.Equal("2.2.0", resultA.Definition.Version.ToString());
        Assert.Equal("2.3.0", (await fixture.Store.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
        Assert.Equal("2.1.0", JsonNode.Parse(resultA.Assessment.Inputs!.PredecessorFormJson!)!["Version"]!.GetValue<string>());
        Assert.Equal("2.2.0", JsonNode.Parse(resultA.Assessment.Inputs.CandidateFormJson)!["Version"]!.GetValue<string>());
        Assert.Equal("f-old-x9", resultA.Assessment.Proposal!.Predecessor.VersionId);
        Assert.Equal("f-new-y8", resultA.Assessment.Proposal.Candidate.VersionId);
        Assert.Equal("Historical inputs observed before publication; the committed Form transition and current Layout membership are not guaranteed. Reassess before acting.", resultA.Assessment.FreshnessLimit);

        var retry = await publisherA.RegisterAndPublishWithImpactAsync(fixture.Candidate, contextA);
        Assert.Same(resultA.Definition, retry.Definition);
        Assert.Equal("predecessor_mismatch", retry.Assessment.UnavailableReason);
        Assert.Null(retry.Assessment.Inputs);
        Assert.Null(retry.Assessment.Compatibility);
        Assert.Null(retry.Assessment.Proposal);
        Assert.Empty(retry.Assessment.Surfaces);
        await Assert.ThrowsAsync<FormDefinitionConflictException>(() => publisherA.RegisterAndPublishWithImpactAsync(
            fixture.Candidate with { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) }, contextA).AsTask());
        var winner = await fixture.Store.GetAsync(new("tenant-a"), new("form.customer"), new(2, 2, 0));
        Assert.Equal(DateTimeOffset.UnixEpoch, winner.CreatedAt);
        Assert.Equal(FormDefinitionStatus.Published, winner.Status);
        Assert.Equal("2.1.0", JsonNode.Parse(resultA.Assessment.Inputs.PredecessorFormJson!)!["Version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_failure_or_unrelated_cancellation_is_unavailable_and_does_not_refuse_publication(bool cancellation)
    {
        var fixture = await ImpactFixture.CreateAsync();
        await using var provider = BuildImpactHost(fixture);
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var source = new ImpactFixture.Source(_ => cancellation ? throw new OperationCanceledException() : throw new IOException("private provider text"));
        var result = await publisher.RegisterAndPublishWithImpactAsync(fixture.Candidate,
            scope.ServiceProvider.FormPublicationContext(fixture.Basis) with { Surfaces = source });
        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.Equal("assessment_input_unavailable", result.Assessment.UnavailableReason);
        Assert.Null(result.Assessment.Inputs);
        Assert.Null(result.Assessment.FreshnessLimit);
        Assert.Null(result.Assessment.Compatibility);
        Assert.Null(result.Assessment.Proposal);
        Assert.Empty(result.Assessment.Surfaces);
        Assert.Equal("2.2.0", (await fixture.Store.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    [Fact]
    public async Task Caller_cancellation_after_assessment_reads_leaves_the_candidate_absent()
    {
        var fixture = await ImpactFixture.CreateAsync();
        await using var provider = BuildImpactHost(fixture);
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var source = new ImpactFixture.Source(_ =>
        {
            cancellation.Cancel();
            return ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-a", []));
        });
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.RegisterAndPublishWithImpactAsync(fixture.Candidate,
            scope.ServiceProvider.FormPublicationContext(fixture.Basis) with { Surfaces = source }, cancellation.Token).AsTask());
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => fixture.Store.GetAsync(new("tenant-a"), new("form.customer"), new(2, 2, 0)).AsTask());
        Assert.Equal("2.1.0", (await fixture.Store.GetCurrentPublishedAsync(new("tenant-a"), new("form.customer")))!.Version.ToString());
    }

    private static ServiceProvider BuildImpactHost(ImpactFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(fixture.Store);
        services.AddSingleton<Harborline.Kernel.SchemaValidation.ISchemaRegistry>(fixture.Schemas);
        services.AddSingleton<IFormPublishedSurfaceSource>(fixture.Context.Surfaces);
        services.AddHarborlineFormsAuthoringPublisher();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Impact_entry_keeps_rule_admission_before_assessment_and_persistence()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var definition = fixture.Candidate with { Overlay = fixture.Candidate.Overlay with { Rules =
            [new("broken", RuleTier.JsonLogic, RuleScope.Schema, "", "{not-json", RuleActionKind.Validate)] } };
        var read = false;
        var source = new ImpactFixture.Source(_ => { read = true; return ValueTask.FromResult<FormPublishedSurfaceRead>(new FormPublishedSurfaceRead.Complete("tenant-a", [])); });
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(fixture.Store);
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        await Assert.ThrowsAsync<FormDefinitionValidationException>(() => publisher.RegisterAndPublishWithImpactAsync(definition,
            fixture.Context with { Surfaces = source }).AsTask());
        Assert.False(read);
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task Actual_atomic_store_failure_does_not_return_a_successful_assessment_result()
    {
        var fixture = await ImpactFixture.CreateAsync();
        // Same tenant, id and version but a different CreatedAt, so the store's identical-retry check fails
        // and the atomic register-and-publish raises a real version conflict.
        var conflicting = fixture.Candidate with { CreatedAt = fixture.Candidate.CreatedAt.AddSeconds(1) };
        await fixture.Store.RegisterAndPublishAsync(conflicting);
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(fixture.Store);
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        await Assert.ThrowsAsync<FormDefinitionConflictException>(() => publisher.RegisterAndPublishWithImpactAsync(
            fixture.Candidate, fixture.Context).AsTask());
        var stored = await fixture.Store.GetAsync(fixture.Candidate.Tenant, fixture.Candidate.Id, fixture.Candidate.Version);
        Assert.Equal(FormDefinitionStatus.Published, stored.Status);
        Assert.Equal(conflicting.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public async Task Legacy_interface_implementation_publishes_without_claiming_unsupported_analysis()
    {
        var fixture = await ImpactFixture.CreateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(fixture.Store);
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        IFormDefinitionAuthoringPublisher legacy = new LegacyPublisher(scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>());
        var result = await legacy.RegisterAndPublishWithImpactAsync(fixture.Candidate, fixture.Context);
        Assert.Equal(FormDefinitionStatus.Published, result.Definition.Status);
        Assert.False(result.Assessment.IsComplete);
        Assert.Null(result.Assessment.Proposal);
        Assert.Equal("publisher_assessment_unsupported", result.Assessment.UnavailableReason);
        Assert.Null(result.Assessment.Inputs);
        Assert.Null(result.Assessment.FreshnessLimit);
        Assert.Null(result.Assessment.Compatibility);
        Assert.Empty(result.Assessment.Surfaces);
    }

    private sealed class LegacyPublisher(IFormDefinitionAuthoringPublisher inner) : IFormDefinitionAuthoringPublisher
    {
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition definition, CancellationToken cancellationToken = default) =>
            inner.RegisterAndPublishAsync(definition, cancellationToken);
    }

    [Fact]
    public async Task AuthoringHost_AdmitsThenPersistsAndPublishesValidDefinition()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("valid", "true");

        var published = await publisher.RegisterAndPublishAsync(definition);

        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        Assert.Equal(published, await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
    }

    [Fact]
    public async Task AuthoringHost_RejectsMalformedRuleBeforeDefinitionPersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("malformed", "{not-json");

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal(FormDefinitionCodes.RulesUncompilable, exception.Code);
        Assert.Null(await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsComputeCycleBeforeDefinitionPersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition(
            "cycle",
            """{"var":"b"}""",
            new RuleDefinition("compute-b", RuleTier.JsonLogic, RuleScope.Field, "b", """{"var":"a"}""", RuleActionKind.Compute));

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal(FormDefinitionCodes.RulesUncompilable, exception.Code);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsNonDraftBeforeDefinitionPersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("already-published", "true") with { Status = FormDefinitionStatus.Published };

        await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsUnknownClassificationBeforeDefinitionPersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = WithFieldAspect(
            Definition("unknown-classification", "true"),
            new AspectOverlay(new ClassificationAspect(
                [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "piii")])));

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Contains("aspect.policy_unresolved", exception.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsUnacknowledgedSensitiveConnectorInputBeforePersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = WithFieldAspect(
            Definition("sensitive-connector", "true"),
            new AspectOverlay(new ClassificationAspect(
                [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "pii")])));
        definition = definition with
        {
            Overlay = definition.Overlay with
            {
                AsyncChecks = [new AsyncValidationCheck("lookup", "registry", "a", "lookup.failed")],
            },
        };

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Contains("aspect.sensitive_input_unacknowledged", exception.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsClassificationRelaxationBeforePersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = WithFieldAspect(
            Definition("classification-relax", "true"),
            new AspectOverlay(new ClassificationAspect(
                [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "phi")]))) with
        {
            Overlay = WithFieldAspect(
                Definition("classification-relax", "true"),
                new AspectOverlay(new ClassificationAspect(
                    [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "phi")]))).Overlay with
            {
                Aspects = new AspectOverlay(new ClassificationAspect(
                    [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "pii")])),
            },
        };

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Contains("aspect.relax_forbidden", exception.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task AuthoringHost_RejectsUnsatisfiableResidencyBeforePersistence()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = WithFieldAspect(
            Definition("residency-conflict", "true"),
            new AspectOverlay(Lifecycle: new LifecycleAspect(
                Residency: new ResidencyRequirement(["CA"])))) with
        {
            Overlay = WithFieldAspect(
                Definition("residency-conflict", "true"),
                new AspectOverlay(Lifecycle: new LifecycleAspect(
                    Residency: new ResidencyRequirement(["CA"])))).Overlay with
            {
                Aspects = new AspectOverlay(Lifecycle: new LifecycleAspect(
                    Residency: new ResidencyRequirement(["US"]))),
            },
        };

        var exception = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Contains("aspect.residency_unsatisfiable", exception.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        services.AddHarborlineFormsAuthoringPublisher();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static FormDefinition WithFieldAspect(FormDefinition definition, AspectOverlay aspects)
    {
        var fields = definition.Overlay.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        fields["a"] = fields["a"] with { Aspects = aspects };
        return definition with { Overlay = definition.Overlay with { Fields = fields } };
    }

    private static FormDefinition Definition(
        string id,
        string expression,
        RuleDefinition? secondRule = null)
    {
        var rules = new List<RuleDefinition>
        {
            new("compute-a", RuleTier.JsonLogic, RuleScope.Field, "a", expression, RuleActionKind.Compute),
        };
        if (secondRule is not null) rules.Add(secondRule);

        return new FormDefinition(
            new FormDefinitionId(id),
            SemanticVersion.Parse("1.0.0"),
            FormDefinitionStatus.Draft,
            new TenantId("tenant-authoring-host"),
            IdentityRef.System,
            new SchemaId("sha256:authoring-host"),
            new HarborlineOverlay(
                new Dictionary<string, FieldOverlay>
                {
                    ["a"] = new(InternationalizedText.FromInvariant("A")),
                    ["b"] = new(InternationalizedText.FromInvariant("B")),
                },
                [new FormSection(
                    "main",
                    InternationalizedText.FromInvariant("Main"),
                    ["a", "b"],
                    new SectionAccess([Harborline.Contracts.Authorization.RoleReference.Domain("author")], [Harborline.Contracts.Authorization.RoleReference.Domain("author")]))],
                rules),
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            SubmitGate: new Harborline.Contracts.Authorization.SubmitGate(
                Role: Harborline.Contracts.Authorization.RoleReference.Domain("author")));
    }
}
