using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewFieldDomainTests
{
    [Fact]
    public async Task Selected_columns_resolve_for_actual_actor_without_leaking_other_columns_or_principals()
    {
        var host = new Host();
        var runtime = host.Runtime();
        foreach (var actor in new[] { " Actor/A ", "actor/a", " Actor/A " })
        {
            var result = await runtime.ExecuteAsync(Host.Request(actor));
            var field = Assert.Single(result.ColumnDomains);
            Assert.Equal("name", field.Key);
            Assert.Equal(actor == " Actor/A " ? "a" : "b", Assert.Single(field.Value.Values!));
            Assert.Equal(FieldEditorKind.SingleValue, field.Value.Editor);
        }
        Assert.Equal("tenant-a", host.RequestedTenant);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("schema")]
    [InlineData("field")]
    [InlineData("runtime")]
    public async Task Invalid_bound_descriptor_refuses(string mismatch)
    {
        var host = new Host { Mismatch = mismatch };
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await host.Runtime().ExecuteAsync(Host.Request(" Actor/A ")));
        Assert.Equal("field.binding_unresolved", Assert.Single(error.Refusals).Code);
    }

    [Fact]
    public async Task Bound_read_role_requires_typed_authority()
    {
        var result = await new Host { Restricted = true }.Runtime().ExecuteAsync(Host.Request(" Actor/A "));
        Assert.Empty(result.ColumnDomains);
    }

    private sealed class Host : IViewDefinitionSource, IViewOpenGate, IViewKindRegistry, IViewRecordTypeRegistry,
        IViewAccessFilter, IViewMeasureCatalog, IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority
    {
        public string? Mismatch { get; init; }
        public bool Restricted { get; init; }
        public string? RequestedTenant { get; private set; }
        public ViewQueryRuntime Runtime() => new(this, this, this, this, this, new InMemoryViewRowSource([]), this,
            TimeProvider.System, Mismatch == "runtime" ? null : new ValueDomainRuntime(this, this, TimeProvider.System));
        public static ViewQueryRequest Request(string actor) => new("tenant-a", "view", actor, new(0, 10), new("table", new Dictionary<ViewShapeRole, string>()));
        public ValueTask<ViewDefinition?> ResolvePublishedHeadAsync(string tenant, string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ViewDefinition?>(new(new(key, "1", tenant, ViewCascadeLayer.Base, JsonSerializer.SerializeToElement(new { }), []),
                1, "View", "record", ViewOwnershipTier.System, "read", new([new("name", 100)], [], null, null, null)));
        public ValueTask<ViewAuthority> AuthorizeAsync(ViewDefinition definition, string principal, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ViewAuthority(true, []));
        ValueTask<ViewKindDescriptor?> IViewKindRegistry.ResolveAsync(string kind, CancellationToken cancellationToken)
            => ValueTask.FromResult<ViewKindDescriptor?>(new(kind, "renderer", []));
        public ValueTask<IReadOnlyList<ViewKindDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ViewKindDescriptor>>([]);
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string recordType, CancellationToken cancellationToken = default)
            => ResolveAsync("legacy-not-scoped", recordType, cancellationToken);
        public ValueTask<ViewRecordTypeDescriptor?> ResolveAsync(string tenant, string recordType, CancellationToken cancellationToken = default)
        {
            RequestedTenant = tenant;
            var binding = new FieldBindingDefinition(new("text", "1", new Dictionary<string, string>()),
                new(false, 0, 1, Restricted ? ["reader"] : [], new(LiteralValues: ["a", "b"])));
            return ValueTask.FromResult<ViewRecordTypeDescriptor?>(new(recordType,
                new Dictionary<string, ViewRecordFieldKind> { ["name"] = ViewRecordFieldKind.Text, ["unselected"] = ViewRecordFieldKind.Text },
                Mismatch == "tenant" ? "foreign" : tenant, Mismatch == "schema" ? null : "schema-1",
                new Dictionary<string, FieldBindingDefinition> { [Mismatch == "field" ? "unselected" : "name"] = binding }));
        }
        public ValueTask<ViewFilter> BuildAsync(string tenant, string principal, string recordType, DateTimeOffset at, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ViewFilter.Equal("name", "a"));
        ValueTask<ViewMeasureDescriptor?> IViewMeasureCatalog.ResolveAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ViewMeasureResult> EvaluateAsync(ViewMeasureBinding binding, IReadOnlyList<ViewRow> rows, DateTimeOffset evaluatedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public TenantId Tenant => new("tenant-a");
        public string Revision => "1";
        public bool IsComplete => true;
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default) => ValueTask.FromResult<IFieldDomainSnapshot>(this);
        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => null;
        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme) => null;
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain, FieldDomainMember member, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(member.Value == (scope.Principal == " Actor/A " ? "a" : "b"));
    }
}
