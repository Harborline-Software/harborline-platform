using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.FieldRuntime;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Harborline.Foundation.Forms.Engine.Tests;

internal sealed class AuthoringFieldFixture(TenantId tenant, string schema, params string[] names)
    : IFormFieldBindingSource, IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority, IAuthenticatedActorContext
{
    public string UserId => "author";
    public IReadOnlyList<string> Roles => ["author"];
    public TenantMetadata Tenant { get; } = new() { Id = tenant, Name = "Test tenant", Status = TenantStatus.Active };

    public ValueTask<FormSchemaFieldBindings?> ResolveAsync(TenantId requestedTenant, string schemaRef, CancellationToken cancellationToken = default)
    {
        if (requestedTenant != tenant || schemaRef != schema) return ValueTask.FromResult<FormSchemaFieldBindings?>(null);
        var fields = names.ToDictionary(name => name, _ => new FieldBindingDefinition(
            new("text", "1", new Dictionary<string, string>()), new(false, 0, 1, [], null)), StringComparer.Ordinal);
        return ValueTask.FromResult<FormSchemaFieldBindings?>(new(tenant, schema, fields));
    }

    TenantId IFieldDomainSnapshot.Tenant => tenant;
    public string Revision => "fixture-1";
    public bool IsComplete => true;
    public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme) => null;
    public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => null;
    public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId requestedTenant, CancellationToken cancellationToken = default)
        => requestedTenant == tenant ? ValueTask.FromResult<IFieldDomainSnapshot>(this) : throw new InvalidOperationException("Unexpected tenant.");
    public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain, FieldDomainMember member, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("This fixture declares no value domains.");

    public static void Add(IServiceCollection services, TenantId tenant, string schema, params string[] names)
    {
        var fixture = new AuthoringFieldFixture(tenant, schema, names);
        services.AddSingleton<IAuthenticatedActorContext>(fixture);
        services.AddSingleton<IFormFieldBindingSource>(fixture);
        services.AddSingleton<IFieldKindRuntime>(new FieldKindRuntime(new FieldKindRegistry([new("text", "1", null)])));
        services.AddSingleton<IFieldDomainRuntime>(new ValueDomainRuntime(fixture, fixture, TimeProvider.System));
    }
}
