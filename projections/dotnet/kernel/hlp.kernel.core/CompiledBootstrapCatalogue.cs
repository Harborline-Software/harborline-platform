namespace Harborline.Kernel.Core;

/// <summary>Refusal codes raised by <see cref="CompiledBootstrapCatalogue"/>.</summary>
public static class KernelBootstrapErrors
{
    /// <summary>A package tried to redefine a shape that the kernel compiles in.</summary>
    public const string CompiledShapeReplacement = "kernel.compiled-shape-replacement";
}

/// <summary>The stable identity of a bootstrap shape, such as <c>kernel.record-type</c>; compared ordinally.</summary>
/// <param name="Value">The identity string.</param>
public readonly record struct CompiledShapeIdentity(string Value)
{
    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}

/// <summary>The value kind of one member of a compiled bootstrap shape.</summary>
public enum CompiledMemberKind
{
    /// <summary>Free text.</summary>
    Text,
    /// <summary>A stable machine key.</summary>
    Key,
    /// <summary>A version string.</summary>
    Version,
    /// <summary>A content digest.</summary>
    Digest,
    /// <summary>An expression evaluated by the rule runtime.</summary>
    Expression,
    /// <summary>One value from a closed set.</summary>
    Enum,
    /// <summary>A boolean.</summary>
    Flag,
    /// <summary>A reference to another shape's instance, named by <see cref="CompiledShapeMember.Target"/>.</summary>
    Reference,
}

/// <summary>One member of a compiled bootstrap shape.</summary>
/// <param name="Key">The member's key, unique within its shape.</param>
/// <param name="Kind">The member's value kind.</param>
/// <param name="Required">True when every instance must carry a value.</param>
/// <param name="Many">True when the member holds a list rather than one value.</param>
/// <param name="Target">For <see cref="CompiledMemberKind.Reference"/>, the key or identity of the referenced shape; otherwise null.</param>
public sealed record CompiledShapeMember(
    string Key,
    CompiledMemberKind Kind,
    bool Required,
    bool Many,
    string? Target);

/// <summary>A shape the kernel compiles in, so it resolves before, and cannot be replaced by, any catalogue content.</summary>
/// <param name="Identity">The shape's stable identity.</param>
/// <param name="Key">The shape's key; packages may not claim it, case-insensitively.</param>
/// <param name="Name">The display name.</param>
/// <param name="Revision">The compiled revision, starting at 1.</param>
/// <param name="Members">The shape's members in declaration order.</param>
public sealed record CompiledBootstrapShape(
    CompiledShapeIdentity Identity,
    string Key,
    string Name,
    int Revision,
    IReadOnlyList<CompiledShapeMember> Members);

/// <summary>The host's catalogue, consulted only for shapes outside the compiled floor.</summary>
public interface IKernelCatalogueReader
{
    /// <summary>Returns the catalogue shape for <paramref name="identity"/>, or null when the catalogue has none.</summary>
    ValueTask<CompiledBootstrapShape?> ReadAsync(
        CompiledShapeIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>A package declared a shape whose key or identity belongs to the compiled floor.</summary>
/// <param name="identity">The compiled shape the package tried to replace.</param>
public sealed class CompiledShapeReplacementException(CompiledShapeIdentity identity)
    : InvalidOperationException($"A package cannot replace compiled bootstrap shape '{identity}'.")
{
    /// <summary>Always <see cref="KernelBootstrapErrors.CompiledShapeReplacement"/>.</summary>
    public string Code { get; } = KernelBootstrapErrors.CompiledShapeReplacement;
    /// <summary>The compiled shape the package tried to replace.</summary>
    public CompiledShapeIdentity Identity { get; } = identity;
}

/// <summary>The immutable pre-catalogue floor resolved before any host catalogue read.</summary>
public sealed class CompiledBootstrapCatalogue
{
    /// <summary>The compiled shape of a definition package.</summary>
    public static readonly CompiledShapeIdentity DefinitionPackage = new("kernel.definition-package");
    /// <summary>The compiled shape of a record type.</summary>
    public static readonly CompiledShapeIdentity RecordType = new("kernel.record-type");
    /// <summary>The compiled shape of a field on a record type.</summary>
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

    /// <summary>Layers the compiled floor over <paramref name="reader"/>; throws <see cref="ArgumentNullException"/> when it is null.</summary>
    public CompiledBootstrapCatalogue(IKernelCatalogueReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>A snapshot of every compiled floor shape.</summary>
    public static IReadOnlyCollection<CompiledBootstrapShape> Shapes => Floor.Values.ToArray();

    /// <summary>True when <paramref name="key"/> names a floor shape by key or identity, ignoring case.</summary>
    public static bool IsCompiledKey(string key) => FloorShapeFor(key) is not null;

    // A floor shape claimed by its key or its identity, ignoring case.
    private static CompiledBootstrapShape? FloorShapeFor(string? key) =>
        Floor.Values.FirstOrDefault(shape =>
            string.Equals(shape.Key, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shape.Identity.Value, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the compiled shape for <paramref name="identity"/> without touching the catalogue, else the catalogue's shape or null.</summary>
    public async ValueTask<CompiledBootstrapShape?> ResolveAsync(
        CompiledShapeIdentity identity,
        CancellationToken cancellationToken = default) =>
        Floor.TryGetValue(identity, out var compiled)
            ? compiled
            : await _reader.ReadAsync(identity, cancellationToken).ConfigureAwait(false);

    /// <summary>Throws <see cref="CompiledShapeReplacementException"/> when any of <paramref name="identities"/> names a floor shape, ignoring case.</summary>
    public static void RefusePackageReplacement(IEnumerable<CompiledShapeIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var shape = identities.Select(identity => FloorShapeFor(identity.Value)).FirstOrDefault(found => found is not null);
        if (shape is not null) throw new CompiledShapeReplacementException(shape.Identity);
    }
}
