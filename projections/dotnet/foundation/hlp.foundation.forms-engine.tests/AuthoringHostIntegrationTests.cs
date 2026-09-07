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
            DateTimeOffset.UnixEpoch);
    }
}
