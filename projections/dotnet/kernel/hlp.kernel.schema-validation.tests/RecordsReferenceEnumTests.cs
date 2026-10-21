using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsReferenceEnumTests
{
    // DES-0015 records-ck-10 admits one/many and block/orphan/cascade only.
    // Literal out-of-domain integers exercise direct .NET callers, bypassing JSON parsing.
    [Theory]
    [InlineData(-1, 0, "records.reference.cardinality_invalid", "/fields/0/reference/cardinality")]
    [InlineData(2, 0, "records.reference.cardinality_invalid", "/fields/0/reference/cardinality")]
    [InlineData(int.MaxValue, 0, "records.reference.cardinality_invalid", "/fields/0/reference/cardinality")]
    [InlineData(0, -1, "records.reference.on_delete_invalid", "/fields/0/reference/on_delete")]
    [InlineData(0, 3, "records.reference.on_delete_invalid", "/fields/0/reference/on_delete")]
    [InlineData(0, int.MaxValue, "records.reference.on_delete_invalid", "/fields/0/reference/on_delete")]
    public async Task Undefined_enum_values_are_refused_by_validator_and_compiler(
        int cardinality, int onDelete, string code, string pointer)
    {
        var candidate = Candidate(cardinality, onDelete);
        var validator = new RecordsIntentValidator();

        var refusal = Assert.Single(validator.Validate(candidate, null));
        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.JsonPointer);

        var compiled = await new RecordTypeSchemaCompiler(validator).CompileAsync(candidate, null);
        Assert.Null(compiled.JsonSchemaText);
        var compilerRefusal = Assert.Single(compiled.Refusals);
        Assert.Equal(code, compilerRefusal.Code);
        Assert.Equal(pointer, compilerRefusal.JsonPointer);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    public async Task Every_declared_cardinality_and_delete_behaviour_combination_is_admitted(
        int cardinality, int onDelete)
    {
        var candidate = Candidate(cardinality, onDelete);
        var validator = new RecordsIntentValidator();

        Assert.Empty(validator.Validate(candidate, null));
        var compiled = await new RecordTypeSchemaCompiler(validator).CompileAsync(candidate, null);
        Assert.Empty(compiled.Refusals);
        Assert.NotNull(compiled.JsonSchemaText);
    }

    private static RecordTypeDefinition Candidate(int cardinality, int onDelete)
        => new("eam.work-order", [new("asset", "Asset", Reference: new("eam.asset", null,
            (ReferenceCardinality)cardinality, (ReferenceDeleteBehavior)onDelete))]);
}
