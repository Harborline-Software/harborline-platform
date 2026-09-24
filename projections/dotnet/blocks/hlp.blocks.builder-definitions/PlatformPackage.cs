using System.Text.Json;
using System.Security.Cryptography;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The required bootstrap stages, in their only admissible replay order.</summary>
public enum PlatformSeedStage
{
    /// <summary>The platform package's own catalogue record.</summary>
    PackageRecord = 1,
    /// <summary>The sealed system record types used to store definitions.</summary>
    SystemRecordTypes = 2,
    /// <summary>The Workshop workspace.</summary>
    Workspace = 3,
    /// <summary>The thirteen pillar navigation definitions.</summary>
    Navigation = 4,
    /// <summary>The pillar and catalogue views.</summary>
    Views = 5,
    /// <summary>Platform roles, capabilities, and bindings (never grants).</summary>
    AccessDefinitions = 6,
    /// <summary>Sealed system definitions whose policies have been resolved.</summary>
    SealedDefinitions = 7,
    /// <summary>Resolved governance defaults.</summary>
    GovernanceDefaults = 8,
}

/// <summary>Whether an item's portable content exists or is waiting on a named ruling.</summary>
public enum PlatformPackageContentClassification
{
    /// <summary>The content is present and exportable.</summary>
    Present,
    /// <summary>The content is intentionally absent because its policy is unresolved.</summary>
    Unresolved,
}

/// <summary>Projection-neutral package content or an explicit unresolved absence.</summary>
public sealed class PlatformPackageContent
{
    private PlatformPackageContent(
        PlatformPackageContentClassification classification,
        string? mediaType,
        ReadOnlyMemory<byte> payload,
        string? reference)
    {
        Classification = classification;
        MediaType = mediaType;
        Payload = payload;
        Reference = reference;
    }

    /// <summary>The content's availability classification.</summary>
    public PlatformPackageContentClassification Classification { get; }
    /// <summary>The payload media type when content is present.</summary>
    public string? MediaType { get; }
    /// <summary>The portable payload when content is present.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
    /// <summary>The design reference that must be ruled when content is unresolved.</summary>
    public string? Reference { get; }

    /// <summary>Creates present JSON content, copying the supplied bytes.</summary>
    public static PlatformPackageContent PresentJson(ReadOnlySpan<byte> json)
    {
        var snapshot = json.ToArray();
        using var _ = JsonDocument.Parse(snapshot);
        return new(PlatformPackageContentClassification.Present, "application/json", snapshot, null);
    }

    /// <summary>Creates an explicit absence linked to the unresolved design question.</summary>
    public static PlatformPackageContent Unresolved(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("platform-package-unresolved-reference-required", nameof(reference));
        return new(PlatformPackageContentClassification.Unresolved, null, ReadOnlyMemory<byte>.Empty, reference);
    }
}

/// <summary>One ordered item in a platform package manifest.</summary>
public sealed record PlatformPackageItem
{
    /// <summary>Creates an item and takes immutable snapshots of its dependency list.</summary>
    public PlatformPackageItem(string id, PlatformSeedStage stage, IEnumerable<string> dependencies, PlatformPackageContent content)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("platform-package-item-id-required", nameof(id));
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Stage = stage;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        Content = content;
    }

    /// <summary>The stable seed item identifier.</summary>
    public string Id { get; }
    /// <summary>The item's bootstrap stage.</summary>
    public PlatformSeedStage Stage { get; }
    /// <summary>Identifiers that must already have replayed.</summary>
    public IReadOnlyList<string> Dependencies { get; }
    /// <summary>The portable content or explicit unresolved absence.</summary>
    public PlatformPackageContent Content { get; }
}

/// <summary>A versioned, ordered platform package manifest.</summary>
public sealed record PlatformPackageManifest
{
    /// <summary>Creates a manifest and takes an immutable snapshot of its items.</summary>
    public PlatformPackageManifest(int schemaVersion, string packageKey, string revision, IEnumerable<PlatformPackageItem> items)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        if (string.IsNullOrWhiteSpace(packageKey)) throw new ArgumentException("platform-package-key-required", nameof(packageKey));
        if (string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("platform-package-revision-required", nameof(revision));
        ArgumentNullException.ThrowIfNull(items);
        SchemaVersion = schemaVersion;
        PackageKey = packageKey;
        Revision = revision;
        Items = Array.AsReadOnly(items.ToArray());
    }

    /// <summary>The manifest wire-schema version.</summary>
    public int SchemaVersion { get; }
    /// <summary>The stable package key.</summary>
    public string PackageKey { get; }
    /// <summary>The package revision.</summary>
    public string Revision { get; }
    /// <summary>The items in required replay order.</summary>
    public IReadOnlyList<PlatformPackageItem> Items { get; }
}

/// <summary>Exports the public manifest with stable property and item ordering.</summary>
public static class PlatformPackageExporter
{
    /// <summary>Returns a deterministic UTF-8 JSON representation terminated by one newline.</summary>
    public static byte[] Export(PlatformPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var unsigned = ExportCore(manifest, digest: null);
        var digest = Convert.ToHexStringLower(SHA256.HashData(unsigned));
        return ExportCore(manifest, digest);
    }

    /// <summary>Verifies byte-for-byte canonical export, including its manifest digest metadata.</summary>
    public static bool Verify(PlatformPackageManifest manifest, ReadOnlySpan<byte> candidate)
        => candidate.SequenceEqual(Export(manifest));

    private static byte[] ExportCore(PlatformPackageManifest manifest, string? digest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("packageKey", manifest.PackageKey);
            writer.WriteString("revision", manifest.Revision);
            writer.WriteStartObject("closure");
            writer.WriteStartArray("dependencies");
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartArray("items");
            foreach (var item in manifest.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteNumber("stage", (int)item.Stage);
                writer.WriteStartArray("dependencies");
                foreach (var dependency in item.Dependencies) writer.WriteStringValue(dependency);
                writer.WriteEndArray();
                writer.WriteStartObject("content");
                writer.WriteString("classification", item.Content.Classification == PlatformPackageContentClassification.Present ? "present" : "unresolved");
                if (item.Content.Classification == PlatformPackageContentClassification.Present)
                {
                    writer.WriteString("mediaType", item.Content.MediaType);
                    writer.WritePropertyName("payload");
                    writer.WriteRawValue(item.Content.Payload.Span, skipInputValidation: false);
                }
                else writer.WriteString("reference", item.Content.Reference);
                writer.WriteEndObject();
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

/// <summary>The atomic storage boundary used to replay a validated package into an empty catalogue.</summary>
public interface IPlatformPackageReplayTarget
{
    /// <summary>
    /// Atomically writes all <paramref name="items"/> in list order only when the target is empty.
    /// Returns false without writing any item when it is non-empty. Implementations must commit all
    /// items or none, including under concurrency and failure.
    /// </summary>
    ValueTask<bool> TryApplyToEmptyAsync(IReadOnlyList<PlatformPackageItem> items, CancellationToken cancellationToken = default);
}

/// <summary>The result of manifest validation or replay.</summary>
/// <param name="Succeeded">Whether validation and replay succeeded.</param>
/// <param name="RefusalCode">The stable refusal code, or <see langword="null"/> on success.</param>
/// <param name="ItemId">The responsible item identifier, when one exists.</param>
public sealed record PlatformPackageReplayResult(bool Succeeded, string? RefusalCode, string? ItemId)
{
    /// <summary>A successful validation or replay.</summary>
    public static PlatformPackageReplayResult Success { get; } = new(true, null, null);

    internal static PlatformPackageReplayResult Refused(string code, string? itemId = null) => new(false, code, itemId);
}

/// <summary>Validates and replays platform package manifests without projection-specific behavior.</summary>
public static class PlatformPackageReplayer
{
    /// <summary>Validates stable identity, order, dependency, and content-availability invariants.</summary>
    public static PlatformPackageReplayResult Validate(PlatformPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Items.Count == 0)
            return PlatformPackageReplayResult.Refused("platform-seed-manifest-empty");
        if (manifest.Items[0].Stage != PlatformSeedStage.PackageRecord)
            return PlatformPackageReplayResult.Refused("platform-seed-package-record-first", manifest.Items[0].Id);
        var allIds = new HashSet<string>(manifest.Items.Select(item => item.Id), StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        PlatformSeedStage? priorStage = null;
        var packageRecordSeen = false;

        foreach (var item in manifest.Items)
        {
            if (seenIds.Contains(item.Id))
                return PlatformPackageReplayResult.Refused("platform-seed-item-duplicate", item.Id);
            if (item.Stage == PlatformSeedStage.PackageRecord)
            {
                if (packageRecordSeen)
                    return PlatformPackageReplayResult.Refused("platform-seed-package-record-count-invalid", item.Id);
                packageRecordSeen = true;
            }
            if (!Enum.IsDefined(item.Stage))
                return PlatformPackageReplayResult.Refused("platform-seed-stage-unknown", item.Id);
            if (priorStage is not null && item.Stage < priorStage)
                return PlatformPackageReplayResult.Refused("platform-seed-stage-order-invalid", item.Id);
            foreach (var dependency in item.Dependencies)
            {
                if (!allIds.Contains(dependency))
                    return PlatformPackageReplayResult.Refused("platform-seed-dependency-missing", item.Id);
                if (!seenIds.Contains(dependency))
                    return PlatformPackageReplayResult.Refused("platform-seed-dependency-not-replayed", item.Id);
            }
            if (item.Content.Classification == PlatformPackageContentClassification.Unresolved)
                return PlatformPackageReplayResult.Refused("platform-seed-content-unresolved", item.Id);
            seenIds.Add(item.Id);
            priorStage = item.Stage;
        }

        return PlatformPackageReplayResult.Success;
    }

    /// <summary>Validates the complete manifest, requires an empty target, then writes in manifest order.</summary>
    public static async ValueTask<PlatformPackageReplayResult> ReplayAsync(
        PlatformPackageManifest manifest,
        IPlatformPackageReplayTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var validation = Validate(manifest);
        if (!validation.Succeeded) return validation;
        if (!await target.TryApplyToEmptyAsync(manifest.Items, cancellationToken).ConfigureAwait(false))
            return PlatformPackageReplayResult.Refused("platform-seed-target-not-empty");
        return PlatformPackageReplayResult.Success;
    }
}
