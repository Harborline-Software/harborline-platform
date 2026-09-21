using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The released artifact's identity as published, independent of any install.</summary>
/// <param name="Id">The published package key.</param>
/// <param name="Version">The published revision.</param>
/// <param name="Digest">The published content digest: 64 lowercase hexadecimal SHA-256 characters.</param>
public sealed record PublishedArtifact(string Id, string Version, string Digest)
{
    /// <summary>Reads the published identity out of a canonical <see cref="PlatformPackageExporter"/> document.</summary>
    public static PublishedArtifact FromExport(ReadOnlySpan<byte> export)
    {
        using var document = JsonDocument.Parse(export.ToArray());
        var root = document.RootElement;
        return new(
            root.GetProperty("packageKey").GetString() ?? throw new InvalidDataException("install-receipt-artifact-key-missing"),
            root.GetProperty("revision").GetString() ?? throw new InvalidDataException("install-receipt-artifact-revision-missing"),
            root.GetProperty("digest").GetProperty("value").GetString() ?? throw new InvalidDataException("install-receipt-artifact-digest-missing"));
    }
}

/// <summary>
/// One package as the installed node reports it back. <paramref name="Witness"/> is the node's own
/// record of the install that wrote this package: the install mints it, nothing else can, and it is
/// what makes a receipt written by an install distinguishable from one written by hand or by a fixture.
/// </summary>
/// <param name="Id">The package key read back from the installed state.</param>
/// <param name="Version">The version read back from the installed state.</param>
/// <param name="Digest">The digest recomputed from the installed state, never the digest that was requested.</param>
/// <param name="Witness">The node's install witness for this package.</param>
public sealed record InstalledPackage(string Id, string Version, string Digest, string Witness);

/// <summary>A capability the installed packages declare, with the outcome the installed bindings predict.</summary>
/// <param name="Name">The declared capability name.</param>
/// <param name="ExpectedAllowed">What the installed bindings and grants predict the node's gate will decide.</param>
public sealed record DeclaredCapability(string Name, bool ExpectedAllowed);

/// <summary>A record the install wrote, with the digest the node reports back for it.</summary>
/// <param name="Id">The record identifier.</param>
/// <param name="Digest">The digest recomputed from the stored record.</param>
public sealed record InstalledRecord(string Id, string Digest);

/// <summary>Everything an install receipt reports, read from the node after the install committed.</summary>
/// <param name="Packages">Installed package identities, versions and digests.</param>
/// <param name="Capabilities">The capability set the installed packages declare, with predicted outcomes.</param>
/// <param name="SealedOperationalTypes">The sealed operational record type ids the installed state carries.</param>
/// <param name="SealedTraits">The sealed trait ids the installed state carries.</param>
/// <param name="Records">The record digests the install wrote.</param>
public sealed record InstalledState(
    IReadOnlyList<InstalledPackage> Packages,
    IReadOnlyList<DeclaredCapability> Capabilities,
    IReadOnlyList<string> SealedOperationalTypes,
    IReadOnlyList<string> SealedTraits,
    IReadOnlyList<InstalledRecord> Records);

/// <summary>The installed node an install receipt is written from and checked against.</summary>
public interface IInstalledNode
{
    /// <summary>
    /// Reads committed installed state. Implementations derive every value from persisted state; an
    /// implementation that returns what the install requested defeats the receipt.
    /// </summary>
    ValueTask<InstalledState> ReadInstalledStateAsync(CancellationToken cancellationToken = default);

    /// <summary>This node's gate decision for one declared capability, as the gate decides it now.</summary>
    ValueTask<bool> AuthorizeAsync(string capability, CancellationToken cancellationToken = default);
}

/// <summary>What one install emitted: the released artifact it consumed and the state it left behind.</summary>
/// <param name="Artifact">Field 1 — immutable released artifact identity.</param>
/// <param name="Installed">Field 2 — installed identity, with digests read back from the installed state.</param>
/// <param name="Capabilities">Fields 3 and 4 — the declared capability set and its expected authorization outcome.</param>
/// <param name="SealedOperationalTypes">The sealed operational record types the install resolved.</param>
/// <param name="SealedTraits">The sealed traits the install resolved.</param>
/// <param name="Records">Field 5 — the record digests the install wrote.</param>
public sealed record InstallReceipt(
    PublishedArtifact Artifact,
    IReadOnlyList<InstalledPackage> Installed,
    IReadOnlyList<DeclaredCapability> Capabilities,
    IReadOnlyList<string> SealedOperationalTypes,
    IReadOnlyList<string> SealedTraits,
    IReadOnlyList<InstalledRecord> Records);

/// <summary>Exports and parses the receipt document with stable property and collection ordering.</summary>
public static class InstallReceiptDocument
{
    /// <summary>Returns a deterministic UTF-8 JSON receipt terminated by one newline, digest last.</summary>
    public static byte[] Export(InstallReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var unsigned = ExportCore(receipt, digest: null);
        return ExportCore(receipt, Convert.ToHexStringLower(SHA256.HashData(unsigned)));
    }

    /// <summary>
    /// Parses a receipt document and verifies its own digest. Returns a refusal code instead of a
    /// receipt when the document is not a receipt or has been edited after it was written.
    /// </summary>
    public static (InstallReceipt? Receipt, string? RefusalCode) Parse(ReadOnlySpan<byte> document)
    {
        var bytes = document.ToArray();
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(bytes); }
        catch (JsonException) { return (null, "install-receipt-malformed"); }
        using (parsed)
        {
            try
            {
                var root = parsed.RootElement;
                var receipt = new InstallReceipt(
                    new PublishedArtifact(
                        Text(root.GetProperty("artifact"), "id"),
                        Text(root.GetProperty("artifact"), "version"),
                        Text(root.GetProperty("artifact"), "digest")),
                    [.. root.GetProperty("installed").EnumerateArray().Select(item => new InstalledPackage(
                        Text(item, "id"), Text(item, "version"), Text(item, "digest"), Text(item, "witness")))],
                    [.. root.GetProperty("capabilities").EnumerateArray().Select(item => new DeclaredCapability(
                        Text(item, "name"), item.GetProperty("expectedAllowed").GetBoolean()))],
                    [.. root.GetProperty("sealedOperationalTypes").EnumerateArray().Select(item => item.GetString()!)],
                    [.. root.GetProperty("sealedTraits").EnumerateArray().Select(item => item.GetString()!)],
                    [.. root.GetProperty("records").EnumerateArray().Select(item => new InstalledRecord(
                        Text(item, "id"), Text(item, "digest")))]);
                // The receipt is canonical, so re-exporting a faithful parse reproduces the exact bytes.
                // Any edit to any field, including the digest itself, fails this comparison.
                return bytes.AsSpan().SequenceEqual(Export(receipt))
                    ? (receipt, null)
                    : (null, "install-receipt-digest-mismatch");
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or NullReferenceException)
            {
                return (null, "install-receipt-malformed");
            }
        }
    }

    private static string Text(JsonElement element, string property)
        => element.GetProperty(property).GetString() ?? throw new InvalidOperationException(property);

    private static byte[] ExportCore(InstallReceipt receipt, string? digest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartObject("artifact");
            writer.WriteString("id", receipt.Artifact.Id);
            writer.WriteString("version", receipt.Artifact.Version);
            writer.WriteString("digest", receipt.Artifact.Digest);
            writer.WriteEndObject();
            writer.WriteStartArray("installed");
            foreach (var package in receipt.Installed.OrderBy(package => package.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", package.Id);
                writer.WriteString("version", package.Version);
                writer.WriteString("digest", package.Digest);
                writer.WriteString("witness", package.Witness);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("capabilities");
            foreach (var capability in receipt.Capabilities.OrderBy(capability => capability.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", capability.Name);
                writer.WriteBoolean("expectedAllowed", capability.ExpectedAllowed);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("sealedOperationalTypes");
            foreach (var id in receipt.SealedOperationalTypes.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("sealedTraits");
            foreach (var id in receipt.SealedTraits.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("records");
            foreach (var record in receipt.Records.OrderBy(record => record.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", record.Id);
                writer.WriteString("digest", record.Digest);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (digest is not null)
            {
                writer.WriteStartObject("digest");
                writer.WriteString("algorithm", "sha256");
                writer.WriteString("value", digest);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
}

/// <summary>Writes the receipt an install emits. Every field but the artifact is read back from the node.</summary>
public static class InstallReceiptWriter
{
    /// <summary>
    /// Reads the committed installed state and returns the canonical receipt document. This never asks
    /// the node's gate for an outcome: the expected outcome comes from the installed declarations, and
    /// the checker is what compares it against the gate.
    /// </summary>
    public static async ValueTask<byte[]> WriteAsync(
        PublishedArtifact artifact,
        IInstalledNode node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(node);
        var state = await node.ReadInstalledStateAsync(cancellationToken).ConfigureAwait(false);
        return InstallReceiptDocument.Export(new InstallReceipt(
            artifact, state.Packages, state.Capabilities, state.SealedOperationalTypes, state.SealedTraits, state.Records));
    }
}

/// <summary>The outcome of checking one receipt: accepted, or refused with the field that refused named.</summary>
/// <param name="Accepted">Whether the receipt matched the installed node in every field.</param>
/// <param name="RefusalCode">The stable refusal code, or <see langword="null"/> when accepted.</param>
/// <param name="Field">The receipt field, capability or id the refusal names.</param>
public sealed record InstallReceiptCheck(bool Accepted, string? RefusalCode, string? Field)
{
    /// <summary>An accepted receipt.</summary>
    public static InstallReceiptCheck Pass { get; } = new(true, null, null);

    internal static InstallReceiptCheck Refuse(string code, string? field = null) => new(false, code, field);
}

/// <summary>Compares a receipt against the node it claims to describe, and against the design corpus.</summary>
public static class InstallReceiptChecker
{
    /// <summary>
    /// Refuses a receipt that does not match the installed node in every field, naming the field. A
    /// receipt that no install on this node wrote is refused on its witness, which is why a hand-written
    /// or fixture-written document cannot pass: nothing outside the install mints a witness.
    /// </summary>
    public static async ValueTask<InstallReceiptCheck> CheckAsync(
        ReadOnlyMemory<byte> document,
        PublishedArtifact published,
        IInstalledNode node,
        SealedTypeCorpus corpus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(corpus);

        var (receipt, parseRefusal) = InstallReceiptDocument.Parse(document.Span);
        if (receipt is null) return InstallReceiptCheck.Refuse(parseRefusal!, "digest");

        if (receipt.Artifact.Id != published.Id) return InstallReceiptCheck.Refuse("install-receipt-artifact-mismatch", "artifact.id");
        if (receipt.Artifact.Version != published.Version) return InstallReceiptCheck.Refuse("install-receipt-artifact-mismatch", "artifact.version");
        if (receipt.Artifact.Digest != published.Digest) return InstallReceiptCheck.Refuse("install-receipt-artifact-mismatch", "artifact.digest");

        var state = await node.ReadInstalledStateAsync(cancellationToken).ConfigureAwait(false);
        var installed = state.Packages.ToDictionary(package => package.Id, StringComparer.Ordinal);

        foreach (var claimed in receipt.Installed)
        {
            if (!installed.TryGetValue(claimed.Id, out var actual))
                return InstallReceiptCheck.Refuse("install-receipt-installed-unknown", $"installed[{claimed.Id}]");
            // The witness is checked first: a document no install on this node wrote is not a receipt,
            // whatever else it happens to say correctly.
            if (claimed.Witness != actual.Witness)
                return InstallReceiptCheck.Refuse("install-receipt-not-written-by-install", $"installed[{claimed.Id}].witness");
            if (claimed.Version != actual.Version)
                return InstallReceiptCheck.Refuse("install-receipt-installed-version-mismatch", $"installed[{claimed.Id}].version");
            if (claimed.Digest != actual.Digest)
                return InstallReceiptCheck.Refuse("install-receipt-installed-digest-mismatch", $"installed[{claimed.Id}].digest");
            // The released artifact and what resolved for it must be the same bytes; this is the binding
            // the whole receipt exists for.
            if (claimed.Id == published.Id && actual.Digest != published.Digest)
                return InstallReceiptCheck.Refuse("install-receipt-installed-digest-mismatch", $"installed[{claimed.Id}].digest");
        }
        foreach (var actual in state.Packages)
            if (!receipt.Installed.Any(claimed => claimed.Id == actual.Id))
                return InstallReceiptCheck.Refuse("install-receipt-installed-missing", $"installed[{actual.Id}]");

        var declared = state.Capabilities.ToDictionary(capability => capability.Name, StringComparer.Ordinal);
        foreach (var claimed in receipt.Capabilities)
        {
            if (!declared.TryGetValue(claimed.Name, out var actual))
                return InstallReceiptCheck.Refuse("install-receipt-capability-not-declared", $"capabilities[{claimed.Name}]");
            if (claimed.ExpectedAllowed != actual.ExpectedAllowed)
                return InstallReceiptCheck.Refuse("install-receipt-capability-mismatch", $"capabilities[{claimed.Name}].expectedAllowed");
            var decided = await node.AuthorizeAsync(claimed.Name, cancellationToken).ConfigureAwait(false);
            if (decided != claimed.ExpectedAllowed)
                return InstallReceiptCheck.Refuse("install-receipt-authorization-mismatch", claimed.Name);
        }
        foreach (var actual in state.Capabilities)
            if (!receipt.Capabilities.Any(claimed => claimed.Name == actual.Name))
                return InstallReceiptCheck.Refuse("install-receipt-capability-missing", $"capabilities[{actual.Name}]");

        var records = state.Records.ToDictionary(record => record.Id, StringComparer.Ordinal);
        foreach (var claimed in receipt.Records)
        {
            if (!records.TryGetValue(claimed.Id, out var actual))
                return InstallReceiptCheck.Refuse("install-receipt-record-unknown", $"records[{claimed.Id}]");
            if (claimed.Digest != actual.Digest)
                return InstallReceiptCheck.Refuse("install-receipt-record-digest-mismatch", $"records[{claimed.Id}]");
        }
        foreach (var actual in state.Records)
            if (!receipt.Records.Any(claimed => claimed.Id == actual.Id))
                return InstallReceiptCheck.Refuse("install-receipt-record-missing", $"records[{actual.Id}]");

        var typeRefusal = CompareCorpus(receipt.SealedOperationalTypes, state.SealedOperationalTypes, corpus.OperationalTypes, "sealed-type", "sealedOperationalTypes");
        if (typeRefusal is not null) return typeRefusal;
        return CompareCorpus(receipt.SealedTraits, state.SealedTraits, corpus.Traits, "sealed-trait", "sealedTraits") ?? InstallReceiptCheck.Pass;
    }

    /// <summary>
    /// ADR 0100 ruling 15: the receipt reports what installed, and DES-0007 must enumerate exactly that.
    /// A difference in either direction refuses and names the id.
    /// </summary>
    private static InstallReceiptCheck? CompareCorpus(
        IReadOnlyList<string> claimed, IReadOnlyList<string> installed, IReadOnlyList<string> recorded, string code, string field)
    {
        foreach (var id in claimed)
            if (!installed.Contains(id, StringComparer.Ordinal))
                return InstallReceiptCheck.Refuse($"install-receipt-{code}-not-installed", $"{field}[{id}]");
        foreach (var id in installed)
            if (!claimed.Contains(id, StringComparer.Ordinal))
                return InstallReceiptCheck.Refuse($"install-receipt-{code}-missing", $"{field}[{id}]");
        foreach (var id in claimed)
            if (!recorded.Contains(SealedTypeCorpus.Key(id), StringComparer.Ordinal))
                return InstallReceiptCheck.Refuse($"install-receipt-{code}-not-in-record", $"{field}[{id}]");
        var keys = claimed.Select(SealedTypeCorpus.Key).ToArray();
        foreach (var name in recorded)
            if (!keys.Contains(name, StringComparer.Ordinal))
                return InstallReceiptCheck.Refuse($"install-receipt-{code}-not-reported", $"{field}[{name}]");
        return null;
    }
}

/// <summary>
/// DES-0007 `platform-package-ck-3` and `platform-package-ck-4` as the control corpus enumerates them.
/// This is the only place a design record is read; the checker compares, it does not interpret.
/// </summary>
/// <param name="OperationalTypes">The sealed operational record types `ck-3` enumerates, as keys.</param>
/// <param name="Traits">The sealed traits `ck-4` enumerates, as keys, excluding those the row itself says the seed omits.</param>
public sealed record SealedTypeCorpus(IReadOnlyList<string> OperationalTypes, IReadOnlyList<string> Traits)
{
    // The row cell names a type in backticks and everything else in lowercase, dotted or underscored
    // form, so a display name is a backticked run of capitalised words and nothing else is.
    private const string DisplayName = @"`([A-Z][A-Za-z]*(?: [A-Z][A-Za-z]*)*)`";

    /// <summary>The comparison key for a sealed id or display name: `Work Item` and `x.y.work-item` both key to `work-item`.</summary>
    public static string Key(string idOrName)
    {
        var tail = idOrName[(idOrName.LastIndexOf('.') + 1)..];
        return tail.Replace(' ', '-').ToLowerInvariant();
    }

    /// <summary>Reads the two rows out of the DES-0007 design record's Markdown.</summary>
    public static SealedTypeCorpus ReadDesignRecord(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return new(Row(markdown, "platform-package-ck-3"), Row(markdown, "platform-package-ck-4"));
    }

    /// <summary>Reads the two rows out of a control checkout.</summary>
    public static SealedTypeCorpus ReadControlRepository(string controlRepositoryPath)
        => ReadDesignRecord(File.ReadAllText(Path.Combine(controlRepositoryPath, "designs", "DES-0007-platform-package", "design.md")));

    private static string[] Row(string markdown, string id)
    {
        var line = markdown.Split('\n').FirstOrDefault(candidate => candidate.StartsWith($"| {id} |", StringComparison.Ordinal))
            ?? throw new InvalidDataException($"install-receipt-design-row-missing:{id}");
        var cell = string.Join('|', line.Split('|')[3..]);
        var named = System.Text.RegularExpressions.Regex.Matches(cell, DisplayName)
            .Select(match => (Key: Key(match.Groups[1].Value), Omitted: IsOmitted(cell, match.Index)))
            .ToArray();
        // ADR 0097 decision 2 keeps `Inspectable` out of the seed and the row says so in its own
        // words, so the row's own omission clause is what excludes it, not a rule invented here. A
        // name the row mentions again later is still excluded: one omission clause covers the name.
        var omitted = named.Where(entry => entry.Omitted).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        return [.. named.Select(entry => entry.Key).Where(key => !omitted.Contains(key)).Distinct(StringComparer.Ordinal)];
    }

    private static bool IsOmitted(string cell, int index)
    {
        var next = cell.IndexOf('`', cell.IndexOf('`', index) + 1) + 1;
        var following = cell[next..];
        var stop = following.IndexOf(';', StringComparison.Ordinal);
        return (stop < 0 ? following : following[..stop]).Contains("omits from the platform seed", StringComparison.Ordinal);
    }
}
