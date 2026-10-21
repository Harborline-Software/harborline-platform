using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The version-specific reader of the platform package's record creation default (decision K6, approved 2026-10-10): the
/// <c>creationGate</c> member of the <c>platform-package-ck-14</c> governance defaults. The released revision
/// <see cref="LegacyRevision"/> spells the default <c>"form-only"</c>, and its bytes and signatures are preserved, so that
/// exact spelling is read only for that revision. Every new revision spells the DES-0004 literal set (<c>form_only</c>,
/// <c>role</c>, <c>engine</c>) exactly. Any other value, a value spelled for another revision, a non-string value and an
/// absent member fail: no published revision of this contract defines a default for an absent <c>creationGate</c>.
/// </summary>
public static class PlatformCreationGateCompatibility
{
    /// <summary>The released platform package revision whose bytes are preserved with the legacy spelling.</summary>
    public const string LegacyRevision = "1.0.0";

    /// <summary>The exact spelling the released <see cref="LegacyRevision"/> carries for <c>form_only</c>.</summary>
    public const string LegacyFormOnlySpelling = "form-only";

    /// <summary>The DES-0004 spelling every new platform package revision carries for the default.</summary>
    public const string FormOnlySpelling = "form_only";

    /// <summary>The refusal raised for a missing, unknown or other-revision creation gate default.</summary>
    public const string Malformed = "platform-package-creation-gate-malformed";

    /// <summary>
    /// The <c>creationGate</c> spelling the seed writes for <paramref name="packageRevision"/>: the released spelling for
    /// <see cref="LegacyRevision"/>, so its bytes never change, and <c>form_only</c> for every new revision.
    /// </summary>
    public static string SeedSpelling(string packageRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRevision);
        return packageRevision == LegacyRevision ? LegacyFormOnlySpelling : FormOnlySpelling;
    }

    /// <summary>
    /// Reads the record creation default from a platform package export, by the export's own <c>revision</c>. Throws
    /// <see cref="InvalidDataException"/> with <see cref="Malformed"/> for a malformed export, a missing or non-string
    /// value, a value outside the revision's spelling set, or a <c>ck-1</c> version that disagrees with the revision.
    /// </summary>
    public static RecordCreationGate ReadCreationGate(ReadOnlySpan<byte> export)
    {
        try
        {
            using var document = JsonDocument.Parse(export.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("revision", out var revisionElement) || revisionElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw MalformedGate();
            var revision = revisionElement.GetString();
            if (string.IsNullOrEmpty(revision)) throw MalformedGate();

            var package = Payload(items, "platform-package-ck-1");
            if (!package.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String
                || version.GetString() != revision)
                throw MalformedGate();

            var defaults = Payload(items, "platform-package-ck-14");
            if (!defaults.TryGetProperty("creationGate", out var gate) || gate.ValueKind != JsonValueKind.String)
                throw MalformedGate();
            var spelling = gate.GetString();
            if (revision == LegacyRevision)
                return spelling == LegacyFormOnlySpelling ? RecordCreationGate.FormOnly : throw MalformedGate();
            return spelling switch
            {
                FormOnlySpelling => RecordCreationGate.FormOnly,
                "role" => RecordCreationGate.Role,
                "engine" => RecordCreationGate.Engine,
                _ => throw MalformedGate(),
            };
        }
        catch (JsonException)
        {
            throw MalformedGate();
        }
        catch (InvalidOperationException)
        {
            throw MalformedGate();
        }
    }

    // The single present payload object of the item with this id.
    private static JsonElement Payload(JsonElement items, string id)
    {
        var matches = items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var itemId) && itemId.ValueKind == JsonValueKind.String && itemId.GetString() == id)
            .ToArray();
        if (matches.Length != 1
            || !matches[0].TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw MalformedGate();
        return payload;
    }

    private static InvalidDataException MalformedGate() => new(Malformed);
}
