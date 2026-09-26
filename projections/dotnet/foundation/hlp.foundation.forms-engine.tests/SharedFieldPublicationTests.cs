using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.FieldRuntime;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine.Authoring;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FieldHost = Harborline.Foundation.Forms.Engine.Tests.SharedFieldBindingTests.FieldHost;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SharedFieldPublicationTests
{
    [Theory]
    [InlineData("taxonomy", null)]
    [InlineData("query", null)]
    [InlineData("missing-caller", "field.value_domain_principal_required")]
    [InlineData("blank-caller", "field.value_domain_principal_required")]
    [InlineData("foreign-tenant", "field.value_domain_tenant_mismatch")]
    [InlineData("inactive-tenant", "field.value_domain_tenant_required")]
    [InlineData("no-tenant", "field.value_domain_tenant_required")]
    [InlineData("missing-kinds", "field.binding_unresolved")]
    [InlineData("missing-domains", "field.binding_unresolved")]
    [InlineData("missing-bindings", "field.binding_unresolved")]
    public async Task Publication_uses_authenticated_tenant_and_complete_field_ports(string scenario, string? code)
    {
        var host = new FieldHost
        {
            SchemaRef = "sha256:publication-fields",
            Domain = scenario == "taxonomy" ? new(TaxonomyScheme: new("scheme", "1"))
                : new(RecordQuery: new("status", "{\"==\":[{\"var\":\"enabled\"},true]}")),
            Records = new Dictionary<string, IReadOnlyList<FieldDomainMember>>
            {
                ["status"] = [new("allowed", "a", System.Text.Json.JsonSerializer.SerializeToElement(new { enabled = true })),
                    new("hidden", "b", System.Text.Json.JsonSerializer.SerializeToElement(new { enabled = false }))],
            },
        };
        var definition = Harness.CreateDefinition(host.SchemaRef, host.Tenant) with
        {
            Status = FormDefinitionStatus.Draft,
            SubmitGate = new Harborline.Contracts.Authorization.SubmitGate(
                Role: Harborline.Contracts.Authorization.RoleReference.Domain("admin")),
        };
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        if (scenario != "missing-caller")
            services.AddSingleton<IAuthenticatedActorContext>(new PublicationActor(
                scenario == "foreign-tenant" ? new("foreign") : host.Tenant,
                scenario == "blank-caller" ? " " : "alice", scenario));
        if (scenario != "missing-bindings") services.AddSingleton<IFormFieldBindingSource>(host);
        if (scenario != "missing-kinds") services.AddSingleton<IFieldKindRuntime>(
            new FieldKindRuntime(new FieldKindRegistry([new("declared", "1", null)])));
        if (scenario != "missing-domains") services.AddSingleton<IFieldDomainRuntime>(new ValueDomainRuntime(host, host, TimeProvider.System));
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();
        if (code is null)
        {
            var published = await publisher.RegisterAndPublishAsync(definition);
            Assert.Equal(FormDefinitionStatus.Published, published.Status);
            Assert.NotNull(await store.GetCurrentPublishedAsync(host.Tenant, definition.Id));
            Assert.NotEmpty(host.Actors);
            Assert.All(host.Actors, actor => Assert.Equal("alice", actor));
        }
        else
        {
            var error = await Assert.ThrowsAsync<FieldAdmissionException>(() => publisher.RegisterAndPublishAsync(definition).AsTask());
            Assert.Contains(error.Refusals, refusal => refusal.Code == code);
            Assert.Null(await store.GetCurrentPublishedAsync(host.Tenant, definition.Id));
            await Assert.ThrowsAsync<FormDefinitionNotFoundException>(() => store.GetAsync(host.Tenant, definition.Id, definition.Version).AsTask());
        }
    }

    [Theory]
    [InlineData("taxonomy", "field.value_domain_source_unresolved")]
    [InlineData("query", "field.value_domain_source_unresolved")]
    [InlineData("widening", "field.value_domain_widened")]
    public async Task Bound_publication_refuses_unresolved_or_widened_domain_before_any_persistence(
        string scenario, string expectedCode)
    {
        var host = new FieldHost
        {
            SchemaRef = "sha256:publication-fields",
            Domain = scenario switch
            {
                "taxonomy" => new(TaxonomyScheme: new("missing-scheme", "1")),
                "query" => new(RecordQuery: new("missing-record-type", "true")),
                _ => new(LiteralValues: ["allowed", "hidden"]),
            },
        };
        var definition = Harness.CreateDefinition(host.SchemaRef, host.Tenant) with
        {
            Status = FormDefinitionStatus.Draft,
            SubmitGate = new Harborline.Contracts.Authorization.SubmitGate(
                Role: Harborline.Contracts.Authorization.RoleReference.Domain("admin")),
            Authoring = scenario == "widening"
                ? new(new Dictionary<string, FormFieldAuthoringMetadata>
                {
                    ["name"] = new("text", true, options: ["outside"]),
                    ["secret"] = new("text", false),
                })
                : null,
        };
        var services = new ServiceCollection();
        services.AddSingleton<IFormDefinitionStore, InMemoryFormDefinitionStore>();
        services.AddSingleton<IAuthenticatedActorContext>(new PublicationActor(host.Tenant));
        services.AddSingleton<IFormFieldBindingSource>(host);
        services.AddSingleton<IFieldKindRuntime>(new FieldKindRuntime(new FieldKindRegistry(
            [new("declared", "1", null, FieldScalarValueShape.Text)])));
        services.AddSingleton<IFieldDomainRuntime>(new ValueDomainRuntime(host, host, TimeProvider.System));
        services.AddHarborlineFormsAuthoringPublisher();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IFormDefinitionAuthoringPublisher>();
        var store = provider.GetRequiredService<IFormDefinitionStore>();

        var error = await Assert.ThrowsAsync<FieldAdmissionException>(
            () => publisher.RegisterAndPublishAsync(definition).AsTask());

        Assert.Contains(error.Refusals, refusal => refusal.Code == expectedCode && refusal.JsonPointer == "/name");
        Assert.Null(await store.GetCurrentPublishedAsync(definition.Tenant, definition.Id));
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            () => store.GetAsync(definition.Tenant, definition.Id, definition.Version).AsTask());
    }

    private sealed class PublicationActor(TenantId tenant, string userId = "alice", string scenario = "") : IAuthenticatedActorContext
    {
        public string UserId => userId;
        public IReadOnlyList<string> Roles => ["admin"];
        public TenantMetadata? Tenant { get; } = scenario == "no-tenant" ? null : new()
        {
            Id = tenant,
            Name = "Publication tenant",
            Status = scenario == "inactive-tenant" ? TenantStatus.Suspended : TenantStatus.Active,
        };
    }
}
