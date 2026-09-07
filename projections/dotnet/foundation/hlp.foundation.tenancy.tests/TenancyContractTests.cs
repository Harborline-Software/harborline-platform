using System.Reflection;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.MultiTenancy.Tests;

public sealed class TenancyContractTests
{
    private sealed record Row(TenantId TenantId, string Name) : IMustHaveTenant;

    private sealed record Context(TenantMetadata? Tenant) : ITenantContext;

    private static IQueryable<Row> Rows(params string[] values) => values
        .Select(value => value.Split(':'))
        .Select(parts => new Row(new TenantId(parts[0]), parts[1]))
        .AsQueryable();

    [Fact]
    public void Metadata_defaults_match_the_contract()
    {
        var metadata = new TenantMetadata { Id = new TenantId("acme"), Name = "acme" };

        Assert.Equal(TenantStatus.Active, metadata.Status);
        Assert.Empty(metadata.Properties);
        Assert.Null(metadata.DisplayName);
        Assert.Null(metadata.Locale);
        Assert.Null(metadata.CreatedAt);
    }

    [Fact]
    public void Context_is_unresolved_when_tenant_is_null() =>
        Assert.False(((ITenantContext)new Context(null)).IsResolved);

    [Fact]
    public void Context_is_resolved_when_tenant_exists() =>
        Assert.True(((ITenantContext)new Context(new TenantMetadata { Id = new TenantId("acme"), Name = "acme" })).IsResolved);

    [Fact]
    public void Context_filter_returns_only_the_active_tenant()
    {
        var context = new Context(new TenantMetadata { Id = new TenantId("acme"), Name = "acme" });

        Assert.Equal(["a", "c"], Rows("acme:a", "beta:b", "acme:c").WhereTenant(context).Select(row => row.Name));
    }

    [Fact]
    public void Explicit_filter_returns_only_the_requested_tenant() =>
        Assert.Equal(["b", "c"], Rows("acme:a", "beta:b", "beta:c").WhereTenant(new TenantId("beta")).Select(row => row.Name));

    [Fact]
    public void Unresolved_context_fails_closed() =>
        Assert.Throws<InvalidOperationException>(() => Rows("acme:a").WhereTenant(new Context(null)).ToList());

    [Fact]
    public void Default_tenant_fails_closed() =>
        Assert.Throws<ArgumentException>(() => Rows("acme:a").WhereTenant(default(TenantId)).ToList());

    [Fact]
    public void System_tenant_fails_closed() =>
        Assert.Throws<ArgumentException>(() => Rows("acme:a").WhereTenant(TenantId.System).ToList());

    [Fact]
    public void Unknown_tenant_returns_no_rows() =>
        Assert.Empty(Rows("acme:a", "beta:b").WhereTenant(new TenantId("unknown")));

    [Fact]
    public void Matching_tenant_returns_all_rows() =>
        Assert.Equal(2, Rows("acme:a", "acme:b").WhereTenant(new TenantId("acme")).Count());

    [Fact]
    public void Filter_composes_without_materializing_the_source()
    {
        var query = Rows("acme:a1", "acme:a2", "beta:b1")
            .Where(row => row.Name.EndsWith('1'))
            .WhereTenant(new TenantId("acme"));

        Assert.Equal("a1", Assert.Single(query).Name);
    }

    [Fact]
    public void Both_filter_overloads_require_IMustHaveTenant()
    {
        var methods = typeof(TenantQueryFilterExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var filters = methods.Where(method => method.Name == nameof(TenantQueryFilterExtensions.WhereTenant)).ToArray();

        Assert.Equal(2, filters.Length);
        Assert.All(filters, method => Assert.Contains(typeof(IMustHaveTenant), method.GetGenericArguments()[0].GetGenericParameterConstraints()));
    }

    [Fact]
    public void Status_wire_vocabulary_is_closed() =>
        Assert.Equal(["Active", "Suspended", "Decommissioning", "Archived"], Enum.GetNames<TenantStatus>());
}
