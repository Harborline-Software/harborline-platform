using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldConstraintRuntimeTests
{
    [Theory]
    [InlineData(false, "incomplete", "field.value_domain_snapshot_incomplete")]
    [InlineData(true, "incomplete", "field.value_domain_snapshot_incomplete")]
    [InlineData(false, "tenant", "field.value_domain_tenant_mismatch")]
    [InlineData(true, "tenant", "field.value_domain_tenant_mismatch")]
    [InlineData(false, "predicate", "field.value_domain_predicate_invalid")]
    [InlineData(true, "predicate", "field.value_domain_predicate_invalid")]
    public async Task Proofs_refuse_untrusted_snapshots_and_malformed_empty_queries(bool narrow, string fault, string code)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [];
        if (fault == "incomplete") fixture.IsComplete = false;
        if (fault == "tenant") fixture.SnapshotTenant = new("tenant-b");
        var floor = new FieldConstraintDefinition(false, 0, null, [], new(LiteralValues: ["a"]));
        var child = floor with { ValueDomain = new(RecordQuery: new("case", "not json")) };
        var runtime = fixture.Runtime();
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () =>
        {
            if (narrow) await runtime.NarrowAsync(floor, child, DomainFixture.Scope, "/proof");
            else await runtime.IntersectAsync([floor, child], DomainFixture.Scope, "/proof");
        });
        Assert.Equal(code, Assert.Single(error.Refusals).Code);
        Assert.Equal("/proof", error.Refusals[0].JsonPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proofs_propagate_cancellation_from_the_authority_owner(bool narrow)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new DomainFixture { OnRead = cancellation.Cancel };
        var floor = new FieldConstraintDefinition(false, 0, null, [], new(LiteralValues: ["a"]));
        var runtime = fixture.Runtime();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (narrow) await runtime.NarrowAsync(floor, floor, DomainFixture.Scope, "/proof", cancellation.Token);
            else await runtime.IntersectAsync([floor], DomainFixture.Scope, "/proof", cancellation.Token);
        });
    }

    [Fact]
    public async Task Query_narrowing_keeps_predicate_attribution_and_refreshes_authority_for_each_proof()
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [DomainFixture.Member("a", "{\"active\":true}"), DomainFixture.Member("b", "{\"active\":false}")];
        const string predicate = "{\"var\":\"field.active\"}";
        var floor = new FieldConstraintDefinition(false, 0, null, [], new(LiteralValues: ["a", "b"]));
        var child = new FieldConstraintDefinition(true, 1, 1, ["reader"], new(RecordQuery: new("case", predicate)));
        var runtime = fixture.Runtime();
        var result = await runtime.NarrowAsync(floor, child, DomainFixture.Scope, "/proof");
        Assert.Equal(new[] { "a" }, result.Values);
        Assert.Equal(new RecordQueryValueSource("case", predicate),
            Assert.Single(result.Sources, source => source.SourceKind == ValueDomainSourceKind.RecordQuery).RecordQuery);
        Assert.Contains(result.Sources, source => source.SourceKind == ValueDomainSourceKind.LiteralSet);
        fixture.Hidden.Add("a");
        var later = await runtime.NarrowAsync(floor, child, DomainFixture.Scope, "/proof");
        Assert.NotNull(later.Values);
        Assert.Empty(later.Values);
        Assert.Equal(new[] { "a" }, result.Values);
        Assert.Equal(2, fixture.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_proof_detaches_all_constraints_roles_and_literal_members_before_opening_a_snapshot(bool narrow)
    {
        var fixture = new DomainFixture();
        var roles = new List<string> { "reader" };
        var literals = new List<string> { "a" };
        var child = new FieldConstraintDefinition(true, 1, 2, roles, new(LiteralValues: literals));
        var constraints = new List<FieldConstraintDefinition> { child };
        fixture.OnOpen = () => { roles.Clear(); literals.Clear(); constraints.Clear(); };
        var runtime = fixture.Runtime();
        var result = narrow
            ? await runtime.NarrowAsync(new(false, 0, null, [], null), child, DomainFixture.Scope, "/proof")
            : await runtime.IntersectAsync(constraints, DomainFixture.Scope, "/proof");
        Assert.True(result.Required);
        Assert.Equal(1, result.MinimumCount);
        Assert.Equal(2, result.MaximumCount);
        Assert.Equal(new[] { "reader" }, result.ReadRoleIds);
        Assert.Equal(new[] { "a" }, result.Values);
    }

    [Fact]
    public async Task Narrowing_cannot_return_a_value_unreadable_through_the_original_domain()
    {
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("secret")];
        fixture.CanRead = (domain, _) => domain.TaxonomyScheme is null;
        var result = await fixture.Runtime().NarrowAsync(
            new(false, 0, null, [], new(TaxonomyScheme: scheme)),
            new(false, 0, null, [], new(LiteralValues: ["secret"])), DomainFixture.Scope, "/proof");
        Assert.NotNull(result.Values);
        Assert.Empty(result.Values);
    }

    [Theory]
    [InlineData("required", "field.requirement_dropped")]
    [InlineData("minimum", "field.multiplicity_widened")]
    [InlineData("maximum", "field.multiplicity_widened")]
    [InlineData("unbounded", "field.multiplicity_widened")]
    [InlineData("roles", "field.read_roles_widened")]
    [InlineData("unrestricted", "field.read_roles_widened")]
    [InlineData("domain", "field.value_domain_widened")]
    [InlineData("empty", "field.constraint_intersection_empty")]
    [InlineData("impossible", "field.constraint_intersection_empty")]
    public async Task Narrowing_refuses_dropped_requirements_or_widened_constraints(string facet, string code)
    {
        var declared = new FieldConstraintDefinition(true, 1, 3, ["reader"], new(LiteralValues: ["a"]));
        var narrowed = facet switch
        {
            "required" => declared with { Required = false },
            "minimum" => declared with { MinimumCount = 0 },
            "maximum" => declared with { MaximumCount = 4 },
            "unbounded" => declared with { MaximumCount = null },
            "roles" => declared with { ReadRoleIds = ["reader", "owner"] },
            "unrestricted" => declared with { ReadRoleIds = [] },
            "domain" => declared with { ValueDomain = null },
            "empty" => declared with { ValueDomain = new(LiteralValues: []) },
            _ => declared with { MinimumCount = 4 },
        };
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await new DomainFixture().Runtime()
            .NarrowAsync(declared, narrowed, DomainFixture.Scope, "/narrow"));
        Assert.Equal(code, Assert.Single(error.Refusals).Code);
        Assert.Equal("/narrow", error.Refusals[0].JsonPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unrestricted_floor_can_be_narrowed_or_remain_unrestricted(bool restrict)
    {
        var floor = new FieldConstraintDefinition(false, 0, null, [], null);
        var child = restrict ? new(true, 1, 2, ["reader"], new ValueDomainDefinition(LiteralValues: ["a"])) : floor;
        var result = await new DomainFixture().Runtime().NarrowAsync(floor, child, DomainFixture.Scope, "/narrow");
        Assert.Equal(restrict, result.Required);
        Assert.Equal(restrict ? 1 : 0, result.MinimumCount);
        Assert.Equal(restrict ? 2 : (int?)null, result.MaximumCount);
        Assert.Equal(restrict ? ["reader"] : Array.Empty<string>(), result.ReadRoleIds);
        if (restrict) Assert.Equal(new[] { "a" }, result.Values);
        else Assert.Null(result.Values);
    }

    [Fact]
    public async Task Narrowing_compares_membership_from_one_snapshot_not_reference_identity()
    {
        var fixture = new DomainFixture();
        var first = new TaxonomySchemeReference("first", "1");
        var second = new TaxonomySchemeReference("second", "7");
        fixture.Schemes[first] = [DomainFixture.Member("a"), DomainFixture.Member("secret")];
        fixture.Schemes[second] = [DomainFixture.Member("secret"), DomainFixture.Member("a")];
        fixture.Hidden.Add("secret");
        var result = await fixture.Runtime().NarrowAsync(
            new(false, 0, null, [], new(TaxonomyScheme: first)),
            new(false, 0, null, [], new(TaxonomyScheme: second)), DomainFixture.Scope, "/narrow");
        Assert.Equal(new[] { "a" }, result.Values);
        Assert.Equal(1, fixture.Opens);
        Assert.Equal(new[] { first, second }, result.Sources.Select(source => source.TaxonomyScheme));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Narrowing_refuses_extra_members_even_when_the_caller_cannot_read_them(bool hidden)
    {
        var fixture = new DomainFixture();
        fixture.Records["case"] = [DomainFixture.Member("a"), DomainFixture.Member("secret")];
        if (hidden) fixture.Hidden.Add("secret");
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await fixture.Runtime().NarrowAsync(
            new(false, 0, null, [], new(LiteralValues: ["a"])),
            new(false, 0, null, [], new(RecordQuery: new("case", "true"))), DomainFixture.Scope, "/narrow"));
        Assert.Equal("field.value_domain_widened", Assert.Single(error.Refusals).Code);
        Assert.Equal("/narrow", error.Refusals[0].JsonPointer);
        Assert.DoesNotContain("secret", error.ToString() + error.Refusals[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intersection_resolves_all_sources_in_one_snapshot_and_preserves_their_attribution()
    {
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("status", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("a"), DomainFixture.Member("b"), DomainFixture.Member("secret")];
        fixture.Records["case"] = [DomainFixture.Member("b"), DomainFixture.Member("c"), DomainFixture.Member("secret")];
        fixture.Hidden.Add("secret");
        var result = await fixture.Runtime().IntersectAsync(
        [
            new(false, 0, null, [], new(TaxonomyScheme: scheme)),
            new(false, 0, null, [], new(RecordQuery: new("case", "true"))),
        ], DomainFixture.Scope, "/constraints");
        Assert.Equal(new[] { "b" }, result.Values);
        Assert.Equal(1, fixture.Opens);
        Assert.Equal("snapshot-1", result.SnapshotRevision);
        Assert.Equal(ValueDomainSourceKind.TaxonomyScheme, result.Sources[0].SourceKind);
        Assert.Equal(scheme, result.Sources[0].TaxonomyScheme);
        Assert.Equal(ValueDomainSourceKind.RecordQuery, result.Sources[1].SourceKind);
        Assert.Equal(new RecordQueryValueSource("case", "true"), result.Sources[1].RecordQuery);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Values!).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<FieldDomainAttribution>)result.Sources).Clear());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_private_intersections_refuse_but_unreadable_nonempty_intersections_are_valid(bool hiddenOverlap)
    {
        var fixture = new DomainFixture();
        fixture.Hidden.Add("secret");
        FieldConstraintDefinition[] constraints =
        [
            new(false, 0, null, [], new(LiteralValues: ["secret"])),
            new(false, 0, null, [], new(LiteralValues: [hiddenOverlap ? "secret" : "other"])),
        ];
        if (hiddenOverlap)
        {
            var result = await fixture.Runtime().IntersectAsync(constraints, DomainFixture.Scope, "/constraints");
            Assert.NotNull(result.Values);
            Assert.Empty(result.Values);
        }
        else
        {
            var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await fixture.Runtime()
                .IntersectAsync(constraints, DomainFixture.Scope, "/constraints"));
            Assert.Equal("field.constraint_intersection_empty", Assert.Single(error.Refusals).Code);
            Assert.DoesNotContain("secret", error.ToString() + error.Refusals[0].Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("multiplicity")]
    [InlineData("required")]
    [InlineData("roles")]
    [InlineData("negative")]
    public async Task Impossible_constraint_floors_are_refused(string conflict)
    {
        FieldConstraintDefinition[] constraints = conflict switch
        {
            "multiplicity" => [new(false, 3, null, [], null), new(false, 0, 2, [], null)],
            "required" => [new(true, 0, null, [], null), new(false, 0, 0, [], null)],
            "roles" => [new(false, 0, null, ["reader"], null), new(false, 0, null, ["owner"], null)],
            _ => [new(false, -1, null, [], null)],
        };
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await new DomainFixture().Runtime()
            .IntersectAsync(constraints, DomainFixture.Scope, "/constraints"));
        Assert.Equal("field.constraint_intersection_empty", Assert.Single(error.Refusals).Code);
        Assert.Equal("/constraints", error.Refusals[0].JsonPointer);
    }

    [Fact]
    public async Task Intersection_promotes_the_required_multiplicity_and_roles_fold()
    {
        var result = await new DomainFixture().Runtime().IntersectAsync(
        [
            new(false, 2, 9, ["reader", "editor"], null),
            new(true, 1, 4, [], null),
            new(false, 3, null, ["editor", "owner"], null),
        ], DomainFixture.Scope, "/constraints");
        Assert.True(result.Required);
        Assert.Equal(3, result.MinimumCount);
        Assert.Equal(4, result.MaximumCount);
        Assert.Equal(new[] { "editor" }, result.ReadRoleIds);
        Assert.Null(result.Values);
        Assert.Empty(result.Sources);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.ReadRoleIds).Clear());
    }
}
