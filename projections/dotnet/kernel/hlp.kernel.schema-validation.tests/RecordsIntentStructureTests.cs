using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsIntentStructureTests
{
    [Theory]
    [InlineData(false, "/fields/0/constraints/value_domain")]
    [InlineData(true, "/traits/0/slots/0/constraints/value_domain")]
    public void Constraint_domains_require_exactly_one_source_even_before_slot_binding(bool inSlot, string jsonPointer)
    {
        var mixed = new ValueDomainDefinition(["a"], new("scheme.example", "1.0.0"));
        var constraints = new FieldConstraintDefinition(false, 0, 1, [], mixed);
        var definition = Definition() with
        {
            Fields = inSlot ? [] : [Field() with { Constraints = constraints }],
            Traits = inSlot
                ? [new("trait.example", "1.0.0", "Example", [new("value", false, false, constraints)])]
                : [],
        };

        var result = new RecordsIntentValidator(RecordsTestDomains.CreateRuntime()).Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "field.value_domain_source_count" && refusal.JsonPointer == jsonPointer);
    }

    [Theory]
    [InlineData(false, -1, 1)]
    [InlineData(false, 0, -1)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 0, 0)]
    public void Unbound_slot_multiplicity_must_admit_its_required_minimum(bool required, int minimum, int maximum)
    {
        var definition = Definition() with
        {
            Traits =
            [
                new("trait.example", "1.0.0", "Example",
                    [new("value", false, false, new(required, minimum, maximum, [], null))]),
            ],
        };

        var result = new RecordsIntentValidator(RecordsTestDomains.CreateRuntime()).Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == "field.constraint_intersection_empty"
            && refusal.JsonPointer == "/traits/0/slots/0/constraints");
    }

    private static RecordTypeDefinition Definition() => new()
    {
        Envelope = new("definition.example", "1.0.0", "tenant-a", "package-a", "test"),
        RecordTypeId = "records.example", Name = "Example", Key = "example", ClassId = "class.example",
    };

    private static RecordFieldDefinition Field() => new()
    {
        Name = "Value", Key = "value", Kind = new("text", "1.0.0", new Dictionary<string, string>()),
    };
}
