using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

// T-615 slice 4 (DES-0015 records-ck-13, records-ck-38; ADR 0095 ruling 3): a field kind's governance defaults are
// materialized once into a new bound field with provenance, and only a kind revision declaring the retention_clock
// capability may start a Record Type's retention clock.
public sealed class RecordsGovernanceTests
{
    private static readonly FieldGovernanceDefinition TextDefaults = new(true, false, true, "internal");

    [Fact]
    public void records_ck_38_a_new_bound_field_takes_its_kinds_defaults_and_provenance()
    {
        var materialized = new RecordFieldDefaults(Kinds()).Materialize(new("order",
        [
            new("reference", "Reference", Binding("text", "1.0.0")),
            new("lines", "Lines", Binding("count", "1.0.0")),
            new("memo", "Memo"),
        ]));

        Assert.Equal(TextDefaults, materialized.Fields[0].Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), materialized.Fields[0].DefaultsProvenance);
        Assert.Null(materialized.Fields[1].Governance);
        Assert.Equal(new FieldKindDefaultProvenance("count", "1.0.0"), materialized.Fields[1].DefaultsProvenance);
        Assert.Equal(new FieldDefinition("memo", "Memo"), materialized.Fields[2]);
    }

    [Fact]
    public void records_ck_38_a_field_with_provenance_or_an_unresolved_kind_is_left_as_authored()
    {
        var created = new FieldDefinition("reference", "Reference", Binding("text", "1.0.0"), null, new("text", "1.0.0"));
        var unresolved = new FieldDefinition("code", "Code", Binding("missing", "1.0.0"));

        var materialized = new RecordFieldDefaults(Kinds()).Materialize(new("order", [created, unresolved]));

        Assert.Equal(created, materialized.Fields[0]);
        Assert.Equal(unresolved, materialized.Fields[1]);
    }

    [Fact]
    public async Task records_ck_38_the_clock_field_kind_must_declare_the_capability()
    {
        var kinds = Kinds();
        var compiler = new RecordTypeSchemaCompiler(new RecordsIntentValidator(), kinds, new SharedValueDomainAdmission());

        var declared = await compiler.CompileAsync(Clocked(Binding("date", "1.0.0"), "acquired_on"), null);
        var otherRevision = await compiler.CompileAsync(Clocked(Binding("date", "2.0.0"), "acquired_on"), null);
        var unbound = await compiler.CompileAsync(Clocked(null, "acquired_on"), null);
        var missing = await compiler.CompileAsync(Clocked(Binding("date", "1.0.0"), "disposed_on"), null);

        Assert.Empty(declared.Refusals);
        Assert.NotNull(declared.JsonSchemaText);
        Assert.Equal(("records.retention.clock_capability_absent", "/retention_clock_field_id"), Single(otherRevision));
        Assert.Equal(("records.retention.clock_capability_absent", "/retention_clock_field_id"), Single(unbound));
        Assert.Equal(("records.retention.clock_field_unresolved", "/retention_clock_field_id"), Single(missing));
        Assert.Null(missing.JsonSchemaText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task records_ck_38_mutation_cannot_change_a_registered_revisions_retention_eligibility(bool declared)
    {
        // Oracle: DES-0015 records-ck-38 / ADR 0095 ruling 3, applied to the original revision declaration.
        var capabilities = new List<FieldKindCapability>();
        if (declared) capabilities.Add(FieldKindCapability.RetentionClock);
        var registry = new FieldKindRegistry([new("date", "1.0.0", null, FieldScalarValueShape.Text, capabilities)]);
        var kinds = new FieldKindRuntime(registry);
        var bound = kinds.Bind(new("date", "1.0.0", new Dictionary<string, string>()), "/kind");

        if (declared) capabilities.Clear();
        else capabilities.Add(FieldKindCapability.RetentionClock);
        var exposed = Assert.IsAssignableFrom<IList<FieldKindCapability>>(bound.Kind.Capabilities);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Throws<NotSupportedException>(() => exposed.Add(FieldKindCapability.RetentionClock));

        var compiler = new RecordTypeSchemaCompiler(new RecordsIntentValidator(), kinds, new SharedValueDomainAdmission());
        var draft = await compiler.CompileAsync(Clocked(Binding("date", "1.0.0"), "acquired_on"), null);
        if (declared)
        {
            Assert.Empty(draft.Refusals);
            Assert.NotNull(draft.JsonSchemaText);
        }
        else
        {
            Assert.Equal(("records.retention.clock_capability_absent", "/retention_clock_field_id"), Single(draft));
            Assert.Null(draft.JsonSchemaText);
        }
    }

    private static RecordTypeDefinition Clocked(FieldBindingDefinition? binding, string clock)
        => new("asset", [new("acquired_on", "Acquired on", binding)], RetentionClockFieldId: clock);

    private static (string Code, string Pointer) Single(RecordTypeSchemaDraft draft)
    {
        var refusal = Assert.Single(draft.Refusals);
        return (refusal.Code, refusal.JsonPointer);
    }

    private static FieldKindRuntime Kinds() => new(new FieldKindRegistry([
        new("count", "1.0.0", null, FieldScalarValueShape.Integer),
        new("text", "1.0.0", TextDefaults, FieldScalarValueShape.Text),
        new("date", "1.0.0", null, FieldScalarValueShape.Text, [FieldKindCapability.RetentionClock]),
        new("date", "2.0.0", null, FieldScalarValueShape.Text),
    ]));

    private static FieldBindingDefinition Binding(string kind, string version)
        => new(new(kind, version, new Dictionary<string, string>()), new FieldConstraintDefinition(false, 0, 1, [], null));
}
