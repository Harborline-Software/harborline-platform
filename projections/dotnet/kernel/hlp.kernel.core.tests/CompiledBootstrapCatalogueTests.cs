using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

public sealed class CompiledBootstrapCatalogueTests
{
    [Fact]
    public async Task EmptyStoreResolvesExactlyThreeCompiledShapesBeforeFirstRead()
    {
        var reader = new RecordingReader();
        var catalogue = new CompiledBootstrapCatalogue(reader);

        Assert.Equal(
            [CompiledBootstrapCatalogue.DefinitionPackage, CompiledBootstrapCatalogue.RecordType, CompiledBootstrapCatalogue.Field],
            CompiledBootstrapCatalogue.Shapes.Select(row => row.Identity).ToArray());
        foreach (var shape in CompiledBootstrapCatalogue.Shapes)
            Assert.Equal(shape, await catalogue.ResolveAsync(shape.Identity));
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task Identity_outside_the_floor_resolves_through_the_catalogue_reader()
    {
        var identity = new CompiledShapeIdentity("tenant.invoice");
        var stored = new CompiledBootstrapShape(identity, "invoice", "Invoice", 0, []);
        var reader = new RecordingReader(stored);

        Assert.Same(stored, await new CompiledBootstrapCatalogue(reader).ResolveAsync(identity));
        Assert.Equal([identity], reader.Requested);
    }

    [Fact]
    public void Floor_members_are_exactly_the_DES_0004_section_1_lists()
    {
        AssertShape(CompiledBootstrapCatalogue.DefinitionPackage, "definition-package", [
            ("name", CompiledMemberKind.Text, true, false, null),
            ("key", CompiledMemberKind.Key, true, false, null),
            ("version", CompiledMemberKind.Version, true, false, null),
            ("contract_version", CompiledMemberKind.Version, true, false, null),
            ("provenance", CompiledMemberKind.Enum, true, false, null),
            ("declared_dependencies", CompiledMemberKind.Reference, false, true, "kernel.definition-package"),
            ("channel", CompiledMemberKind.Enum, true, false, null),
            ("digest", CompiledMemberKind.Digest, true, false, null)]);
        AssertShape(CompiledBootstrapCatalogue.RecordType, "record-type", [
            ("name", CompiledMemberKind.Text, true, false, null),
            ("key", CompiledMemberKind.Key, true, false, null),
            ("class_id", CompiledMemberKind.Reference, true, false, "class"),
            ("traits", CompiledMemberKind.Reference, false, true, "trait"),
            ("creation_gate", CompiledMemberKind.Enum, true, false, null),
            ("amendment_policy", CompiledMemberKind.Enum, true, false, null),
            ("history_policy", CompiledMemberKind.Enum, true, false, null),
            ("visibility_policy", CompiledMemberKind.Enum, true, false, null),
            ("retention_policy", CompiledMemberKind.Enum, true, false, null),
            ("retention_clock_field_id", CompiledMemberKind.Reference, false, false, "kernel.field"),
            ("categories", CompiledMemberKind.Text, false, true, null),
            ("package_id", CompiledMemberKind.Reference, true, false, "kernel.definition-package")]);
        AssertShape(CompiledBootstrapCatalogue.Field, "field", [
            ("type_id", CompiledMemberKind.Reference, true, false, "kernel.record-type"),
            ("name", CompiledMemberKind.Text, true, false, null),
            ("key", CompiledMemberKind.Key, true, false, null),
            ("kind", CompiledMemberKind.Enum, true, false, null),
            ("required_condition", CompiledMemberKind.Expression, false, false, null),
            ("default_expression", CompiledMemberKind.Expression, false, false, null),
            ("write_role_id", CompiledMemberKind.Reference, false, false, "role"),
            ("reference_trait_id", CompiledMemberKind.Reference, false, false, "trait"),
            ("personal_data", CompiledMemberKind.Flag, true, false, null),
            ("confidential", CompiledMemberKind.Flag, true, false, null),
            ("masked", CompiledMemberKind.Flag, true, false, null),
            ("classification", CompiledMemberKind.Enum, true, false, null),
            ("unique_in", CompiledMemberKind.Text, false, true, null),
            ("conflict_policy", CompiledMemberKind.Enum, true, false, null),
            ("show_in_lists_hint", CompiledMemberKind.Flag, true, false, null)]);
    }

    [Theory]
    [InlineData("kernel.definition-package")]
    [InlineData("kernel.record-type")]
    [InlineData("kernel.field")]
    public void PackageCannotReplaceCompiledFloor(string identity)
    {
        var error = Assert.Throws<CompiledShapeReplacementException>(() =>
            CompiledBootstrapCatalogue.RefusePackageReplacement([new(identity)]));
        Assert.Equal(KernelBootstrapErrors.CompiledShapeReplacement, error.Code);
        Assert.Equal(identity, error.Identity.Value);
        Assert.Equal($"A package cannot replace compiled bootstrap shape '{identity}'.", error.Message);
    }

    [Theory]
    [InlineData("record-type")]
    [InlineData("Record-Type")]
    [InlineData("kernel.record-type")]
    public void Package_cannot_replace_a_compiled_shape_by_key(string key)
    {
        var error = Assert.Throws<CompiledShapeReplacementException>(() =>
            CompiledBootstrapCatalogue.RefusePackageReplacement([new(key)]));

        Assert.Equal(CompiledBootstrapCatalogue.RecordType, error.Identity);
    }

    [Fact]
    public void Floor_shapes_carry_their_display_names() =>
        Assert.Equal(
            ["Definition Package", "Record Type", "Field"],
            CompiledBootstrapCatalogue.Shapes.Select(shape => shape.Name).ToArray());

    [Fact]
    public void Replacement_check_refuses_a_missing_identity_list() =>
        Assert.Throws<ArgumentNullException>("identities", () => CompiledBootstrapCatalogue.RefusePackageReplacement(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_key_is_not_a_compiled_key(string key) => Assert.False(CompiledBootstrapCatalogue.IsCompiledKey(key));

    [Fact]
    public void Replacement_check_ignores_non_floor_keys() =>
        CompiledBootstrapCatalogue.RefusePackageReplacement([new("tenant-record-type")]);

    private static void AssertShape(
        CompiledShapeIdentity identity,
        string key,
        (string Key, CompiledMemberKind Kind, bool Required, bool Many, string? Target)[] members)
    {
        var shape = Assert.Single(CompiledBootstrapCatalogue.Shapes, item => item.Identity == identity);
        Assert.Equal(key, shape.Key);
        Assert.Equal(members, shape.Members.Select(member =>
            (member.Key, member.Kind, member.Required, member.Many, member.Target)).ToArray());
    }

    private sealed class RecordingReader(CompiledBootstrapShape? stored = null) : IKernelCatalogueReader
    {
        public int Reads => Requested.Count;
        public List<CompiledShapeIdentity> Requested { get; } = [];
        public ValueTask<CompiledBootstrapShape?> ReadAsync(CompiledShapeIdentity identity, CancellationToken cancellationToken = default)
        {
            Requested.Add(identity);
            return ValueTask.FromResult(stored);
        }
    }
}
