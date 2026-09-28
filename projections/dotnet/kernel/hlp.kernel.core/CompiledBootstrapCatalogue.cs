namespace Harborline.Kernel.Core;

public static class KernelBootstrapErrors
{
    public const string CompiledShapeReplacement = "kernel.compiled-shape-replacement";
}

public readonly record struct CompiledShapeIdentity(string Value)
{
    public override string ToString() => Value;
}

public enum CompiledMemberKind
{
    Text,
    Key,
    Version,
    Digest,
    Expression,
    Enum,
    Flag,
    Reference,
}

public sealed record CompiledShapeMember(
    string Key,
    CompiledMemberKind Kind,
    bool Required,
    bool Many,
    string? Target);

public sealed record CompiledBootstrapShape(
    CompiledShapeIdentity Identity,
    string Key,
    string Name,
    int Revision,
    IReadOnlyList<CompiledShapeMember> Members);

public interface IKernelCatalogueReader
{
    ValueTask<CompiledBootstrapShape?> ReadAsync(
        CompiledShapeIdentity identity,
        CancellationToken cancellationToken = default);
}

public sealed class CompiledShapeReplacementException(CompiledShapeIdentity identity)
    : InvalidOperationException($"A package cannot replace compiled bootstrap shape '{identity}'.")
{
    public string Code { get; } = KernelBootstrapErrors.CompiledShapeReplacement;
    public CompiledShapeIdentity Identity { get; } = identity;
}

/// <summary>The immutable pre-catalogue floor resolved before any host catalogue read.</summary>
public sealed class CompiledBootstrapCatalogue
{
    public static readonly CompiledShapeIdentity DefinitionPackage = new("kernel.definition-package");
    public static readonly CompiledShapeIdentity RecordType = new("kernel.record-type");
    public static readonly CompiledShapeIdentity Field = new("kernel.field");

    private static readonly IReadOnlyDictionary<CompiledShapeIdentity, CompiledBootstrapShape> Floor =
        new Dictionary<CompiledShapeIdentity, CompiledBootstrapShape>
        {
            [DefinitionPackage] = new(DefinitionPackage, "definition-package", "Definition Package", 1,
            [
                new("name", CompiledMemberKind.Text, true, false, null),
                new("key", CompiledMemberKind.Key, true, false, null),
                new("version", CompiledMemberKind.Version, true, false, null),
                new("contract_version", CompiledMemberKind.Version, true, false, null),
                new("provenance", CompiledMemberKind.Enum, true, false, null),
                new("declared_dependencies", CompiledMemberKind.Reference, false, true, DefinitionPackage.Value),
                new("channel", CompiledMemberKind.Enum, true, false, null),
                new("digest", CompiledMemberKind.Digest, true, false, null),
            ]),
            [RecordType] = new(RecordType, "record-type", "Record Type", 1,
            [
                new("name", CompiledMemberKind.Text, true, false, null),
                new("key", CompiledMemberKind.Key, true, false, null),
                new("class_id", CompiledMemberKind.Reference, true, false, "class"),
                new("traits", CompiledMemberKind.Reference, false, true, "trait"),
                new("creation_gate", CompiledMemberKind.Enum, true, false, null),
                new("amendment_policy", CompiledMemberKind.Enum, true, false, null),
                new("history_policy", CompiledMemberKind.Enum, true, false, null),
                new("visibility_policy", CompiledMemberKind.Enum, true, false, null),
                new("retention_policy", CompiledMemberKind.Enum, true, false, null),
                new("retention_clock_field_id", CompiledMemberKind.Reference, false, false, Field.Value),
                new("categories", CompiledMemberKind.Text, false, true, null),
                new("package_id", CompiledMemberKind.Reference, true, false, DefinitionPackage.Value),
            ]),
            [Field] = new(Field, "field", "Field", 1,
            [
                new("type_id", CompiledMemberKind.Reference, true, false, RecordType.Value),
                new("name", CompiledMemberKind.Text, true, false, null),
                new("key", CompiledMemberKind.Key, true, false, null),
                new("kind", CompiledMemberKind.Enum, true, false, null),
                new("required_condition", CompiledMemberKind.Expression, false, false, null),
                new("default_expression", CompiledMemberKind.Expression, false, false, null),
                new("write_role_id", CompiledMemberKind.Reference, false, false, "role"),
                new("reference_trait_id", CompiledMemberKind.Reference, false, false, "trait"),
                new("personal_data", CompiledMemberKind.Flag, true, false, null),
                new("confidential", CompiledMemberKind.Flag, true, false, null),
                new("masked", CompiledMemberKind.Flag, true, false, null),
                new("classification", CompiledMemberKind.Enum, true, false, null),
                new("unique_in", CompiledMemberKind.Text, false, true, null),
                new("conflict_policy", CompiledMemberKind.Enum, true, false, null),
                new("show_in_lists_hint", CompiledMemberKind.Flag, true, false, null),
            ]),
        };

    private readonly IKernelCatalogueReader _reader;

    public CompiledBootstrapCatalogue(IKernelCatalogueReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public static IReadOnlyCollection<CompiledBootstrapShape> Shapes => Floor.Values.ToArray();

    public static bool IsCompiledKey(string key) => FloorShapeFor(key) is not null;

    // A floor shape claimed by its key or its identity, ignoring case.
    private static CompiledBootstrapShape? FloorShapeFor(string? key) =>
        Floor.Values.FirstOrDefault(shape =>
            string.Equals(shape.Key, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shape.Identity.Value, key, StringComparison.OrdinalIgnoreCase));

    public async ValueTask<CompiledBootstrapShape?> ResolveAsync(
        CompiledShapeIdentity identity,
        CancellationToken cancellationToken = default) =>
        Floor.TryGetValue(identity, out var compiled)
            ? compiled
            : await _reader.ReadAsync(identity, cancellationToken).ConfigureAwait(false);

    public static void RefusePackageReplacement(IEnumerable<CompiledShapeIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var shape = identities.Select(identity => FloorShapeFor(identity.Value)).FirstOrDefault(found => found is not null);
        if (shape is not null) throw new CompiledShapeReplacementException(shape.Identity);
    }
}
