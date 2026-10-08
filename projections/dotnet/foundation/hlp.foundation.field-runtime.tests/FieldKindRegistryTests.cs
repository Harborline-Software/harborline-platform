using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldKindRegistryTests
{
    [Fact]
    public void The_exact_registered_revision_supplies_its_scalar_shape_and_governance()
    {
        var defaults = new FieldGovernanceDefinition(true, true, false, null);
        var registry = new FieldKindRegistry([new("amount", "1.0.0", defaults, FieldScalarValueShape.Number)]);

        var kind = registry.Resolve(new("amount", "1.0.0", new Dictionary<string, string>()), "/fields/0/kind");

        Assert.Equal(FieldScalarValueShape.Number, kind.ValueShape);
        Assert.Equal(defaults, kind.GovernanceDefaults);
    }

    [Theory]
    [InlineData("amount", "2.0.0")]
    [InlineData("AMOUNT", "1.0.0")]
    [InlineData("text", "1.0.0")]
    public void Unknown_versions_names_and_guessed_builtins_refuse(string name, string version)
    {
        var registry = new FieldKindRegistry([new("amount", "1.0.0", null, FieldScalarValueShape.Number)]);
        var error = Assert.Throws<FieldAdmissionException>(() => registry.Resolve(
            new(name, version, new Dictionary<string, string>()), "/fields/0/kind"));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.kind_unresolved", refusal.Code);
        Assert.Equal("/fields/0/kind", refusal.JsonPointer);
    }

    [Fact]
    public void The_registry_is_detached_from_the_supplied_registration_collection()
    {
        var supplied = new List<AdmittedFieldKind> { new("amount", "1.0.0", null, FieldScalarValueShape.Number) };
        var registry = new FieldKindRegistry(supplied);
        supplied.Clear();

        Assert.Equal("amount", registry.Resolve(
            new("amount", "1.0.0", new Dictionary<string, string>()), "/kind").KindId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registered_capabilities_are_detached_and_cannot_be_mutated_through_resolution(bool declared)
    {
        // Oracle: the exact revision's declaration at registration, independently of either later mutation.
        var capabilities = new List<FieldKindCapability>();
        if (declared) capabilities.Add(FieldKindCapability.RetentionClock);
        var registry = new FieldKindRegistry([new("date", "1.0.0", null, FieldScalarValueShape.Text, capabilities)]);
        var reference = new FieldKindReference("date", "1.0.0", new Dictionary<string, string>());
        var resolved = registry.Resolve(reference, "/kind");

        if (declared) capabilities.Clear();
        else capabilities.Add(FieldKindCapability.RetentionClock);
        Assert.Equal(declared ? new[] { FieldKindCapability.RetentionClock } : Array.Empty<FieldKindCapability>(), resolved.Capabilities);

        var exposed = Assert.IsAssignableFrom<IList<FieldKindCapability>>(resolved.Capabilities);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Throws<NotSupportedException>(() => exposed.Add(FieldKindCapability.RetentionClock));
        if (declared)
            Assert.Throws<NotSupportedException>(() => exposed[0] = (FieldKindCapability)99);
        Assert.Equal(declared ? new[] { FieldKindCapability.RetentionClock } : Array.Empty<FieldKindCapability>(),
            registry.Resolve(reference, "/kind").Capabilities);
    }

    [Fact]
    public void Duplicate_registrations_and_unsupported_shapes_fail_closed()
    {
        var kind = new AdmittedFieldKind("amount", "1.0.0", null, FieldScalarValueShape.Number);
        var duplicate = Assert.Throws<FieldAdmissionException>(() => new FieldKindRegistry([kind, kind]));
        Assert.Equal("field.kind_registration_duplicate", Assert.Single(duplicate.Refusals).Code);

        var unsupported = Assert.Throws<FieldAdmissionException>(() => new FieldKindRegistry(
            [kind with { ValueShape = (FieldScalarValueShape)99 }]));
        Assert.Equal("field.kind_registration_invalid", Assert.Single(unsupported.Refusals).Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    public void Undefined_capabilities_refuse_even_alongside_a_known_capability(int undefined, bool includeKnown)
    {
        // Oracle: requested closed-enum registration property, with literal refusal code and root pointer.
        // This is consistency hardening, not a claim that an unknown value grants retention eligibility.
        FieldKindCapability[] capabilities = includeKnown
            ? [(FieldKindCapability)0, (FieldKindCapability)undefined]
            : [(FieldKindCapability)undefined];

        var error = Assert.Throws<FieldAdmissionException>(() => new FieldKindRegistry(
            [new("date", "1.0.0", null, FieldScalarValueShape.Text, capabilities)]));

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.kind_registration_invalid", refusal.Code);
        Assert.Equal("", refusal.JsonPointer);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void Absent_empty_and_known_capabilities_remain_admitted(int declaration)
    {
        // Oracle: null and empty declare no capabilities; literal zero declares retention_clock.
        FieldKindCapability[]? capabilities = declaration switch
        {
            -1 => null,
            0 => [],
            _ => [(FieldKindCapability)0],
        };
        var registry = new FieldKindRegistry(
            [new("date", "1.0.0", null, FieldScalarValueShape.Text, capabilities)]);

        var resolved = registry.Resolve(new("date", "1.0.0", new Dictionary<string, string>()), "/kind");

        if (declaration == -1)
            Assert.Null(resolved.Capabilities);
        else
            Assert.Equal(declaration == 0 ? Array.Empty<int>() : new[] { 0 },
                resolved.Capabilities!.Select(value => (int)value));
    }

    [Fact]
    public void Admission_refusals_cannot_be_rewritten_by_their_original_list()
    {
        var supplied = new List<FieldRefusal> { new("field.kind_unresolved", "/kind", "Not admitted.") };
        var exception = new FieldAdmissionException(supplied);
        supplied.Clear();
        Assert.Equal("field.kind_unresolved", Assert.Single(exception.Refusals).Code);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<FieldRefusal>)exception.Refusals).Clear());
    }
}
