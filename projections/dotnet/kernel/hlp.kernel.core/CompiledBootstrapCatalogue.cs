namespace Harborline.Kernel.Core;

public static class KernelBootstrapErrors
{
    public const string CompiledShapeReplacement = "kernel.compiled-shape-replacement";
}

public readonly record struct CompiledShapeIdentity(string Value)
{
    public override string ToString() => Value;
}

public sealed record CompiledBootstrapShape(
    CompiledShapeIdentity Identity,
    string Name,
    int Revision);

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
            [DefinitionPackage] = new(DefinitionPackage, "Definition Package", 1),
            [RecordType] = new(RecordType, "Record Type", 1),
            [Field] = new(Field, "Field", 1),
        };

    private readonly IKernelCatalogueReader _reader;

    public CompiledBootstrapCatalogue(IKernelCatalogueReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public static IReadOnlyCollection<CompiledBootstrapShape> Shapes => Floor.Values.ToArray();

    public async ValueTask<CompiledBootstrapShape?> ResolveAsync(
        CompiledShapeIdentity identity,
        CancellationToken cancellationToken = default) =>
        Floor.TryGetValue(identity, out var compiled)
            ? compiled
            : await _reader.ReadAsync(identity, cancellationToken).ConfigureAwait(false);

    public static void RefusePackageReplacement(IEnumerable<CompiledShapeIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var replacement = identities.FirstOrDefault(Floor.ContainsKey);
        if (!string.IsNullOrEmpty(replacement.Value)) throw new CompiledShapeReplacementException(replacement);
    }
}
