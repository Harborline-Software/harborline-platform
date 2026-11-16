using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
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
        AuthoringFieldFixture.Add(services, fixture.Candidate.Tenant, fixture.Candidate.SchemaRef.Value, "name");
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
        fixture = fixture with { Store = FormGateRegisters.Store(), Basis = fixture.Basis with
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
        AuthoringFieldFixture.Add(services, fixture.Candidate.Tenant, fixture.Candidate.SchemaRef.Value, "name");
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
        AuthoringFieldFixture.Add(services, fixture.Candidate.Tenant, fixture.Candidate.SchemaRef.Value, "name");
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        await Assert.ThrowsAsync<DefinitionRefusalException>(() => publisher.RegisterAndPublishWithImpactAsync(definition,
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
        AuthoringFieldFixture.Add(services, fixture.Candidate.Tenant, fixture.Candidate.SchemaRef.Value, "name");
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
        AuthoringFieldFixture.Add(services, fixture.Candidate.Tenant, fixture.Candidate.SchemaRef.Value, "name");
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("form.rules.uncompilable", Assert.Single(exception.Refusals).Code);
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("form.rules.uncompilable", Assert.Single(exception.Refusals).Code);
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

        await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("save-next")]
    public async Task Draft_save_refuses_a_non_Draft_definition_without_a_write(string entry)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var draft = Definition("non-draft-save", "true");
        var seeded = entry == "save-next";
        if (seeded) await store.CreateAsync(draft);
        var published = draft with { Status = FormDefinitionStatus.Published };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            (seeded ? publisher.SaveNextAsync(published) : publisher.CreateAsync(published)).AsTask());

        Assert.Equal("author", error.Stage.ToString().ToLowerInvariant());
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("form_definition.validation_refused", refusal.Code);
        Assert.Equal("/overlay", refusal.Pointer);
        var revisions = new List<FormDefinition>();
        await foreach (var revision in store.ListByTenantAsync(draft.Tenant)) revisions.Add(revision);
        Assert.Equal(seeded ? 1 : 0, revisions.Count);
        Assert.Null(await store.GetCurrentPublishedAsync(draft.Tenant, draft.Id));
    }

    [Theory]
    [InlineData("pages", "form.pages.duplicate_page_id", "/overlay/pages")]
    [InlineData("guard", "form.rules.guard_uncompilable", "/overlay/pages")]
    [InlineData("layout", "form.layout.unknown_density", "/overlay/sections")]
    [InlineData("checks", "form.checks.bad_id", "/overlay/asyncChecks")]
    [InlineData("sensitive", "aspect.sensitive_input_unacknowledged", "/overlay/asyncChecks")]
    [InlineData("submit-gate", "form.submit_gate.form_invalid", "/submit_gate")]
    public async Task Validation_refusal_points_at_the_overlay_member_its_code_names(string fault, string code, string pointer)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var definition = Definition("pointer-" + fault, "true");
        var overlay = definition.Overlay;
        var title = InternationalizedText.FromInvariant("P");
        if (fault == "sensitive")
            overlay = WithFieldAspect(definition, new AspectOverlay(new ClassificationAspect(
                [new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "pii")]))).Overlay;
        definition = fault switch
        {
            "pages" => definition with { Overlay = overlay with { Pages = [new("p1", title, ["main"]), new("p1", title, ["main"])] } },
            "guard" => definition with { Overlay = overlay with { Pages = [new("p1", title, ["main"], "{not-json")] } },
            "layout" => definition with { Overlay = overlay with { Sections =
                [overlay.Sections[0] with { Layout = new SectionLayout(SectionLayoutKind.Stack, Density: "bogus") }] } },
            "checks" => definition with { Overlay = overlay with { AsyncChecks = [new AsyncValidationCheck("", "registry", "a", "lookup.failed")] } },
            "sensitive" => definition with { Overlay = overlay with { AsyncChecks = [new AsyncValidationCheck("lookup", "registry", "a", "lookup.failed")] } },
            "submit-gate" => definition with { SubmitGate = new Harborline.Contracts.Authorization.SubmitGate() },
            _ => throw new InvalidOperationException("Unknown fault."),
        };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => publisher.RegisterAndPublishAsync(definition).AsTask());

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.Pointer);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("policyAdmission")]
    [InlineData("fieldBindings")]
    [InlineData("fieldKinds")]
    [InlineData("fieldDomains")]
    public async Task Publisher_refuses_a_null_port_at_construction(string port)
    {
        await using var provider = BuildHost();
        var services = provider.GetRequiredService<IServiceProvider>();
        T Port<T>(string name) where T : class => name == port ? null! : services.GetRequiredService<T>();

        var exception = Assert.Throws<ArgumentNullException>(() => new FormDefinitionAuthoringPublisher(
            Port<IFormDefinitionStore>("store"), Port<IFormDefinitionPolicyAdmission>("policyAdmission"),
            Port<IFormFieldBindingSource>("fieldBindings"), Port<Harborline.Contracts.Fields.IFieldKindRuntime>("fieldKinds"),
            Port<Harborline.Contracts.Fields.IFieldDomainRuntime>("fieldDomains")));

        Assert.Equal(port, exception.ParamName);
    }

    [Fact]
    public async Task Page_id_shaped_like_a_pointer_is_not_echoed_as_the_pointer()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var definition = Definition("pointer-shaped-page", "true");
        definition = definition with { Overlay = definition.Overlay with
            { Pages = [new("/submit_gate", InternationalizedText.FromInvariant("P"), ["main"], "{not-json")] } };

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => publisher.CreateAsync(definition).AsTask());

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("form.rules.guard_uncompilable", refusal.Code);
        Assert.Equal("/overlay/pages", refusal.Pointer);
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("aspect.policy_unresolved", Assert.Single(exception.Refusals).Code);
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("aspect.sensitive_input_unacknowledged", Assert.Single(exception.Refusals).Code);
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("aspect.relax_forbidden", Assert.Single(exception.Refusals).Code);
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

        var exception = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Equal("aspect.residency_unsatisfiable", Assert.Single(exception.Refusals).Code);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Theory]
    [InlineData("create", "author", "rule", "form.rules.uncompilable", "/overlay/rules")]
    [InlineData("create", "author", "policy", "aspect.policy_unresolved", "/overlay")]
    [InlineData("create", "author", "reference", "field.binding_unresolved", "")]
    [InlineData("save-next", "author", "rule", "form.rules.uncompilable", "/overlay/rules")]
    [InlineData("save-next", "author", "policy", "aspect.policy_unresolved", "/overlay")]
    [InlineData("save-next", "author", "reference", "field.binding_unresolved", "")]
    [InlineData("restore", "author", "rule", "form.rules.uncompilable", "/overlay/rules")]
    [InlineData("restore", "author", "policy", "aspect.policy_unresolved", "/overlay")]
    [InlineData("restore", "author", "reference", "field.binding_unresolved", "")]
    [InlineData("register-publish", "publish", "rule", "form.rules.uncompilable", "/overlay/rules")]
    [InlineData("register-publish", "publish", "policy", "aspect.policy_unresolved", "/overlay")]
    [InlineData("register-publish", "publish", "reference", "field.binding_unresolved", "")]
    [InlineData("publish-stored", "publish", "rule", "form.rules.uncompilable", "/overlay/rules")]
    [InlineData("publish-stored", "publish", "policy", "aspect.policy_unresolved", "/overlay")]
    [InlineData("publish-stored", "publish", "reference", "field.binding_unresolved", "")]
    public async Task Every_validate_gate_refuses_before_write_with_its_boundary(string entry, string phase, string gate, string code, string pointer)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("gate-test", gate == "rule" ? "{not-json" : "true") with { Owner = new("user", "alice") };
        if (gate == "policy") definition = WithFieldAspect(definition,
            new AspectOverlay(new ClassificationAspect([new Tag(DefaultFormFieldGovernanceResolver.DataClassificationSystem, "piii")])));
        if (gate == "reference") definition = definition with { SchemaRef = new("sha256:unknown") };
        var seeded = entry is "restore" or "publish-stored";
        if (seeded) await store.CreateAsync(definition);
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() => (entry switch
        {
            "create" => publisher.CreateAsync(definition),
            "save-next" => publisher.SaveNextAsync(definition),
            "restore" => publisher.RestoreAsDraftAsync(definition.Tenant, definition.Id, definition.Version),
            "register-publish" => publisher.RegisterAndPublishAsync(definition),
            "publish-stored" => publisher.PublishAsync(definition.Tenant, definition.Id, definition.Version),
            _ => throw new InvalidOperationException("Unknown entry."),
        }).AsTask());
        Assert.Equal(phase, error.Stage.ToString().ToLowerInvariant());
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.Pointer);
        if (seeded) Assert.Equal(definition, await store.GetAsync(definition.Tenant, definition.Id, definition.Version));
        else await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
        var revisions = new List<FormDefinition>();
        await foreach (var revision in store.ListByTenantAsync(definition.Tenant)) revisions.Add(revision);
        Assert.Equal(seeded ? 1 : 0, revisions.Count);
        Assert.Null(await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
    }

    [Theory]
    [InlineData("author")]
    [InlineData("publish")]
    public async Task Store_invariant_refusal_uses_boundary_phase_without_a_write(string phase)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var original = Definition("duplicate-section", "true");
        var definition = original with { Overlay = original.Overlay with { Sections = [original.Overlay.Sections[0], original.Overlay.Sections[0]] } };
        var method = typeof(IFormDefinitionAuthoringPublisher).GetMethod(phase == "author" ? "CreateAsync" : "RegisterAndPublishAsync");
        Assert.NotNull(method);
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await ((ValueTask<FormDefinition>)method.Invoke(publisher, [definition, CancellationToken.None])!).AsTask());
        Assert.Equal(phase, error.Stage.ToString().ToLowerInvariant());
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("form_definition.validation_refused", refusal.Code);
        Assert.Equal("/overlay", refusal.Pointer);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    [Fact]
    public async Task Authoring_lifecycle_saves_publishes_and_restores_without_rewriting_history()
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("lifecycle", "true") with { Owner = new("user", "alice") };
        async Task<FormDefinition> Invoke(string name, params object?[] args)
        {
            var method = typeof(IFormDefinitionAuthoringPublisher).GetMethod(name);
            Assert.NotNull(method);
            return await ((ValueTask<FormDefinition>)method.Invoke(publisher, args)!).AsTask();
        }
        var first = await Invoke("CreateAsync", definition, CancellationToken.None);
        var second = await Invoke("SaveNextAsync", definition, CancellationToken.None);
        Assert.Equal("1.0.0", first.Version.ToString());
        Assert.Equal("1.0.1", second.Version.ToString());
        var published = await Invoke("PublishAsync", definition.Tenant, definition.Id, second.Version, CancellationToken.None);
        Assert.Equal(FormDefinitionStatus.Published, published.Status);
        var restored = await Invoke("RestoreAsDraftAsync", definition.Tenant, definition.Id, first.Version, CancellationToken.None);
        Assert.Equal("1.0.2", restored.Version.ToString());
        Assert.Equal(FormDefinitionStatus.Draft, restored.Status);
        Assert.Equal("1.0.0", restored.DerivedFrom!.ToString());
        Assert.Equal(first, await store.GetAsync(definition.Tenant, definition.Id, first.Version));
        Assert.Equal(published, await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
    }

    [Theory]
    [InlineData("restore", "author", "form.lifecycle.system_owned")]
    [InlineData("publish", "publish", "form.lifecycle.transition_illegal")]
    public async Task Lifecycle_store_refusals_keep_code_and_phase(string operation, string phase, string code)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var definition = Definition("lifecycle-refusal", "true");
        await store.CreateAsync(definition);
        if (operation == "publish") await store.WithdrawAsync(definition.Tenant, definition.Id, definition.Version);
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            (operation == "restore"
                ? publisher.RestoreAsDraftAsync(definition.Tenant, definition.Id, definition.Version)
                : publisher.PublishAsync(definition.Tenant, definition.Id, definition.Version)).AsTask());
        Assert.Equal(phase, error.Stage.ToString().ToLowerInvariant());
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal("", refusal.Pointer);
        Assert.Null(await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
    }

    [Theory]
    [InlineData("restore", "author", true)]
    [InlineData("restore", "author", false)]
    [InlineData("publish", "publish", true)]
    [InlineData("publish", "publish", false)]
    public async Task Foreign_tenant_caller_gets_one_refusal_whether_or_not_the_entry_exists(string operation, string phase, bool exists)
    {
        await using var provider = BuildHost();
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        var foreign = Definition("foreign-entry", "{not-json") with { Tenant = new TenantId("tenant-other") };
        if (exists) await store.CreateAsync(foreign);

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            (operation == "restore"
                ? publisher.RestoreAsDraftAsync(foreign.Tenant, foreign.Id, foreign.Version)
                : publisher.PublishAsync(foreign.Tenant, foreign.Id, foreign.Version)).AsTask());

        Assert.Equal(phase, error.Stage.ToString().ToLowerInvariant());
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.value_domain_tenant_mismatch", refusal.Code);
        Assert.Equal("", refusal.Pointer);
    }

    [Fact]
    public async Task Plain_store_invalid_operation_propagates_unchanged()
    {
        var failure = new InvalidOperationException("adapter failure");
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore>(new FailureStore(failure));
        AuthoringFieldFixture.Add(services, new("tenant-authoring-host"), "sha256:authoring-host", "a", "b");
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IFormDefinitionAuthoringPublisher>().CreateAsync(Definition("adapter-failure", "true")).AsTask());
        Assert.Same(failure, error);
    }

    private sealed class FailureStore(InvalidOperationException failure) : IFormDefinitionStore
    {
        public ValueTask<FormDefinition> CreateAsync(FormDefinition definition, CancellationToken ct = default) => throw failure;
        public ValueTask<FormDefinition> GetAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition?> GetCurrentPublishedAsync(TenantId tenant, FormDefinitionId id, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAsync(FormDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RegisterAndPublishAsync(FormDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> PublishAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> DeprecateAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> WithdrawAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask<FormDefinition> RestorePackProjectionAsync(TenantId tenant, FormDefinitionId id, SemanticVersion version, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<FormDefinition> ListByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<FormDefinition> ListCurrentPublishedByTenantAsync(TenantId tenant, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddFormGateRegisters();
        services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        AuthoringFieldFixture.Add(services, new("tenant-authoring-host"), "sha256:authoring-host", "a", "b");
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
