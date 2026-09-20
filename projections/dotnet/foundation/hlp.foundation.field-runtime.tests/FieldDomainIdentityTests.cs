using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldDomainIdentityTests
{
    [Fact]
    public async Task Narrowed_editor_uses_final_readable_set_and_inherited_authority()
    {
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("scheme", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("a"), DomainFixture.Member("b")];
        fixture.CanRead = (domain, member) => domain.TaxonomyScheme is null || member.Value == "a";
        var floor = new FieldConstraintDefinition(true, 0, 1, [], new(TaxonomyScheme: scheme));
        var result = await fixture.Runtime().NarrowAsync(floor, floor with { ValueDomain = new(LiteralValues: ["a", "b"]) }, DomainFixture.Scope, "/field");
        Assert.Equal(new[] { "a" }, result.Values);
        Assert.Equal(FieldEditorKind.SingleValue, result.Editor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Missing_principal_refuses_before_reading_any_domain_source(string? principal)
    {
        // A permissive source adapter must not turn an absent caller into a readable domain.
        var fixture = new DomainFixture();
        var scope = new FieldDomainScope(DomainFixture.Tenant, principal!);

        var exception = await Assert.ThrowsAsync<FieldAdmissionException>(async () =>
            await fixture.Runtime().ResolveAsync(new(LiteralValues: ["visible"]), scope, "/domain"));

        Assert.Equal("field.value_domain_principal_required", Assert.Single(exception.Refusals).Code);
        Assert.Equal("/domain", exception.Refusals[0].JsonPointer);
        Assert.Equal(0, fixture.Opens);
    }

    [Fact]
    public async Task Actual_actor_spelling_controls_membership_without_cross_principal_leakage()
    {
        var fixture = new DomainFixture();
        var runtime = new ValueDomainRuntime(fixture, new ActorAuthority());
        var domain = new ValueDomainDefinition(LiteralValues: ["first", "second"]);
        foreach (var actor in new[] { " Actor/A ", "actor/a", " Actor/A " })
        {
            var resolved = await runtime.ResolveAsync(domain, new(DomainFixture.Tenant, actor), "/field");
            Assert.Equal(actor == " Actor/A " ? "first" : "second", Assert.Single(resolved.Values));
            Assert.Equal(FieldEditorKind.SingleValue, resolved.Editor);
        }
    }

    private sealed class ActorAuthority : IFieldDomainReadAuthority
    {
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(member.Value == (scope.Principal == " Actor/A " ? "first" : "second"));
    }
}
