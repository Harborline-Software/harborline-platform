using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class ValueDomainRuntimeTests
{
    [Theory]
    [InlineData(false, 0, FieldEditorKind.None)]
    [InlineData(false, 1, FieldEditorKind.SingleValue)]
    [InlineData(false, 2, FieldEditorKind.RadioGroup)]
    [InlineData(true, 0, FieldEditorKind.None)]
    [InlineData(true, 1, FieldEditorKind.SingleValue)]
    [InlineData(true, 2, FieldEditorKind.RadioGroup)]
    public async Task Source_and_readable_set_cardinality_choose_the_editor(bool taxonomy, int readable, FieldEditorKind editor)
    {
        var fixture = new DomainFixture();
        string[] values = [" A ", "a", "hidden"];
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = values.Select(value => DomainFixture.Member(value)).ToArray();
        foreach (var value in values.Skip(readable)) fixture.Hidden.Add(value);
        var result = await fixture.Runtime().ResolveAsync(taxonomy ? new(TaxonomyScheme: scheme) : new(LiteralValues: values),
            DomainFixture.Scope, "/domain");
        Assert.Equal(editor, result.Editor);
        Assert.Equal(readable, result.Values.Count);
        Assert.Equal(taxonomy ? ValueDomainSourceKind.TaxonomyScheme : ValueDomainSourceKind.LiteralSet, result.SourceKind);
        if (readable > 0) Assert.Equal(" A ", result.Values[0]);
    }

    [Fact]
    public async Task Literal_resolution_detaches_the_declaration_before_await_and_rechecks_authority_each_call()
    {
        var fixture = new DomainFixture();
        var values = new List<string> { "first", "second", "first" };
        var declaration = new ValueDomainDefinition(LiteralValues: values);
        fixture.OnOpen = () => values.Clear();
        var runtime = fixture.Runtime();
        var first = await runtime.ResolveAsync(declaration, DomainFixture.Scope, "/domain");
        Assert.Equal(new[] { "first", "second" }, first.Values);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Values).Add("changed"));
        fixture.OnOpen = null;
        values.Add("first");
        fixture.Hidden.Add("first");
        var second = await runtime.ResolveAsync(declaration, DomainFixture.Scope, "/domain");
        Assert.Empty(second.Values);
        Assert.Equal(2, first.Values.Count);
    }

    [Fact]
    public async Task Resolution_runs_shared_source_admission()
    {
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await new DomainFixture().Runtime()
            .ResolveAsync(new(LiteralValues: ["x"], TaxonomyScheme: new("status", "1")), DomainFixture.Scope, "/domain"));
        Assert.Equal("field.value_domain_source_count", Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData("tenant", "field.value_domain_tenant_mismatch")]
    [InlineData("incomplete", "field.value_domain_snapshot_incomplete")]
    [InlineData("revision", "field.value_domain_snapshot_incomplete")]
    [InlineData("sentinel", "field.value_domain_tenant_required")]
    public async Task Resolution_requires_a_complete_pinned_tenant_snapshot(string fault, string code)
    {
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("secret")];
        fixture.Hidden.Add("secret");
        if (fault == "tenant") fixture.SnapshotTenant = new("tenant-b");
        if (fault == "incomplete") fixture.IsComplete = false;
        if (fault == "revision") fixture.Revision = "";
        var scope = fault == "sentinel" ? DomainFixture.Scope with { Tenant = TenantId.System } : DomainFixture.Scope;
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await fixture.Runtime().ResolveAsync(
            new(TaxonomyScheme: scheme), scope, "/domain"));
        Assert.Equal(code, Assert.Single(error.Refusals).Code);
        Assert.Equal("/domain", error.Refusals[0].JsonPointer);
        Assert.DoesNotContain("secret", error.ToString() + error.Refusals[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("open")]
    [InlineData("authority")]
    public async Task Cancellation_is_propagated_even_if_an_adapter_ignores_the_token(string when)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("open")];
        if (when == "before") cancellation.Cancel();
        if (when == "open") fixture.OnOpen = cancellation.Cancel;
        if (when == "authority") fixture.OnRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Runtime().ResolveAsync(
            new(TaxonomyScheme: scheme), DomainFixture.Scope, "/domain", cancellation.Token));
    }

    [Fact]
    public async Task An_unresolved_exact_taxonomy_version_refuses_at_the_callers_pointer()
    {
        IFieldDomainRuntime runtime = new ValueDomainRuntime(new TaxonomySource(DomainFixture.Tenant), new ReadAuthority());
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await runtime.ResolveAsync(
            new(TaxonomyScheme: new("case-status", "2.0.0")), DomainFixture.Scope, "/fields/a~1b/domain"));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.value_domain_source_unresolved", refusal.Code);
        Assert.Equal("/fields/a~1b/domain", refusal.JsonPointer);
    }

    [Fact]
    public async Task Taxonomy_resolution_excludes_unreadable_members_before_choosing_the_editor()
    {
        // Removing the runtime's read-authority filter must expose "restricted"
        // and change the editor cardinality, failing both consumer observations.
        var tenant = new TenantId("tenant-a");
        var scope = new FieldDomainScope(tenant, "actor-a");
        IFieldDomainRuntime runtime = new ValueDomainRuntime(new TaxonomySource(tenant), new ReadAuthority());

        var resolved = await runtime.ResolveAsync(
            new(TaxonomyScheme: new("case-status", "1.0.0")), scope, "/fields/status/value_domain");

        Assert.Equal(ValueDomainSourceKind.TaxonomyScheme, resolved.SourceKind);
        Assert.Equal(new[] { "open" }, resolved.Values);
        Assert.Equal(FieldEditorKind.SingleValue, resolved.Editor);
    }

    // Only the host's external snapshot and authority are replaced. Exact-version
    // resolution, filtering, cardinality and editor choice belong to the real runtime.
    private sealed class TaxonomySource(TenantId tenant) : IFieldDomainSource
    {
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId requestedTenant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requestedTenant != tenant) throw new InvalidOperationException("Unexpected tenant.");
            return ValueTask.FromResult<IFieldDomainSnapshot>(new TaxonomySnapshot(tenant));
        }
    }

    private sealed class TaxonomySnapshot(TenantId tenant) : IFieldDomainSnapshot
    {
        public TenantId Tenant => tenant;
        public string Revision => "snapshot-1";
        public bool IsComplete => true;

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => scheme == new TaxonomySchemeReference("case-status", "1.0.0")
                ? [new("open", "concept-open", JsonSerializer.SerializeToElement(new { })),
                   new("restricted", "concept-restricted", JsonSerializer.SerializeToElement(new { }))]
                : null;

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
            => throw new InvalidOperationException("A taxonomy domain must not read a record query.");
    }

    private sealed class ReadAuthority : IFieldDomainReadAuthority
    {
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(member.ResourceId == "concept-open");
        }
    }
}
