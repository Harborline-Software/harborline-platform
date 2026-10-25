using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

// T-615, records-ck-14 (declaration only): a field may name the one domain role that writes it. The name is grammar the
// Records store resolves at publication and installation; it changes neither the compiled record schema nor field identity.
public sealed class RecordFieldWriteRoleTests
{
    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task A_write_role_leaves_the_compiled_record_schema_byte_identical()
    {
        var plain = await Compiler().CompileAsync(Order(null), null);
        var declared = await Compiler().CompileAsync(Order("asset-author"), null);

        Assert.Empty(plain.Refusals);
        Assert.Empty(declared.Refusals);
        Assert.NotNull(plain.JsonSchemaText);
        Assert.Equal(plain.JsonSchemaText, declared.JsonSchemaText);
        Assert.DoesNotContain("write_role", declared.JsonSchemaText!, StringComparison.Ordinal);
    }

    // The literal schemas of an unbound and a reference field (spelled as in RecordsFieldMultiplicitySchemaTests): a write
    // role on either kind of field leaves the bytes exactly as written here.
    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task Unbound_and_reference_fields_with_a_write_role_compile_to_their_literal_schemas()
    {
        const string unboundSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"label":{"type":"string"}},"additionalProperties":false}""";
        const string referenceSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"owner":{"type":"object","properties":{"type":{"const":"person"},"id":{"type":"string","minLength":1}},"required":["type","id"],"additionalProperties":false}},"additionalProperties":false}""";

        var unbound = await Compiler().CompileAsync(new("label-record", [new("label", "Label") { WriteRoleId = "asset-author" }]), null);
        var reference = await Compiler().CompileAsync(new("owned-record", [new("owner", "Owner", Reference:
            new("person", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block)) { WriteRoleId = "asset-author" }]), null);

        Assert.Empty(unbound.Refusals);
        Assert.Empty(reference.Refusals);
        Assert.Equal(unboundSchema, unbound.JsonSchemaText);
        Assert.Equal(referenceSchema, reference.JsonSchemaText);
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public void Declaring_or_dropping_a_write_role_between_versions_is_not_an_identity_change()
    {
        var validator = new RecordsIntentValidator();

        Assert.Empty(validator.Validate(Order("asset-author"), Order(null)));
        Assert.Empty(validator.Validate(Order(null), Order("asset-author")));
        Assert.Empty(validator.Validate(Order("meter-author"), Order("asset-author")));
        // Control: the same comparison does see a real identity change.
        var renamed = Assert.Single(validator.Validate(Order("asset-author") with { RecordTypeId = "invoice" }, Order(null)));
        Assert.Equal(("records.identity.record_type_id_immutable", "/record_type_id"), (renamed.Code, renamed.JsonPointer));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public void The_member_is_optional_init_only_and_part_of_field_equality()
    {
        var field = new FieldDefinition("quantity", "Quantity", Binding()) { WriteRoleId = "asset-author" };

        Assert.Null(new FieldDefinition("quantity", "Quantity", Binding()).WriteRoleId);
        Assert.Equal("asset-author", (field with { DisplayName = "Count" }).WriteRoleId);
        Assert.NotEqual(field, field with { WriteRoleId = null });
        Assert.NotEqual(field, field with { WriteRoleId = "Asset-Author" });
    }

    private static RecordTypeDefinition Order(string? writeRoleId) => new("order",
    [
        new("quantity", "Quantity", Binding()) { WriteRoleId = writeRoleId },
        new("notes", "Notes"),
    ]);

    private static FieldBindingDefinition Binding()
        => new(new FieldKindReference("count", "1.0.0", new Dictionary<string, string>()), new FieldConstraintDefinition(false, 0, 1, [], null));

    private static RecordTypeSchemaCompiler Compiler() => new(new RecordsIntentValidator(), new FieldKindRuntime(new FieldKindRegistry([
        new("count", "1.0.0", null, FieldScalarValueShape.Integer),
    ])), new SharedValueDomainAdmission());
}
