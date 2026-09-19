using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A public, versioned reference; content and secret values never enter this contract.</summary>
/// <param name="Key">Exact, case-sensitive identity.</param>
/// <param name="Revision">Released revision.</param>
/// <param name="Digest">Lowercase SHA-256 of the referenced content.</param>
public sealed record ConfigurationReference(string Key, string Revision, string Digest);

/// <summary>A resolved package and its complete content inventory and direct dependencies.</summary>
/// <param name="Reference">Package manifest reference.</param>
/// <param name="Content">All versioned content references in the package.</param>
/// <param name="Dependencies">Exact package keys resolved in this generation.</param>
public sealed record ConfigurationPackage(ConfigurationReference Reference,
    IReadOnlyList<ConfigurationReference> Content, IReadOnlyList<string> Dependencies);

/// <summary>The selected package owner of one effective definition.</summary>
/// <param name="DefinitionKey">The definition being selected.</param>
/// <param name="PackageKey">The package whose content supplies that definition.</param>
public sealed record ConfigurationOwnership(string DefinitionKey, string PackageKey);

/// <summary>Host-resolved, complete tenant configuration. Empty policy is explicit, never a default.</summary>
/// <param name="TenantKey">Tenant to which the resolution belongs.</param>
/// <param name="ActivePackageKeys">Explicitly active roots.</param>
/// <param name="Packages">The complete transitive closure, including active roots.</param>
/// <param name="Ownership">Exactly one owner for every distinct content key in the closure.</param>
/// <param name="PlatformContract">The applicable platform contract revision and digest.</param>
/// <param name="Policies">Every applicable resolved configuration policy reference.</param>
public sealed record ResolvedConfiguration(string TenantKey, IReadOnlyList<string> ActivePackageKeys,
    IReadOnlyList<ConfigurationPackage> Packages, IReadOnlyList<ConfigurationOwnership> Ownership,
    ConfigurationReference PlatformContract, IReadOnlyList<ConfigurationReference> Policies);

/// <summary>An immutable generation identity and its canonical public constituent references.</summary>
public sealed class ConfigurationGeneration
{
    private readonly byte[] canonical;

    private ConfigurationGeneration(byte[] canonical)
    {
        this.canonical = canonical;
        Digest = Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    /// <summary>Identifies the complete generation, never an individual package version.</summary>
    public string Digest { get; }

    /// <summary>The algorithm used by this version of the generation contract.</summary>
    public string Algorithm { get; } = "sha256";

    /// <summary>Returns the canonical reference document as a detached snapshot.</summary>
    public JsonElement References
    {
        get
        {
            using var document = JsonDocument.Parse(canonical);
            return document.RootElement.Clone();
        }
    }

    /// <summary>Validates closure and ownership before deriving a deterministic generation.</summary>
    public static ConfigurationGeneration Resolve(ResolvedConfiguration resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        Required(resolved.TenantKey);
        // Snapshot every collection before validation and hashing; no retained host-owned lists.
        var roots = Unique(resolved.ActivePackageKeys, key => key);
        ArgumentNullException.ThrowIfNull(resolved.Packages);
        var packages = Unique(resolved.Packages.Select(Snapshot), package => package.Reference.Key);
        var ownership = Unique(resolved.Ownership, owner => owner.DefinitionKey);
        var contract = Validate(resolved.PlatformContract);
        var policies = Unique(resolved.Policies, reference => Validate(reference).Key);
        if (roots.Length == 0) throw new ArgumentException("configuration-active-packages-required");
        var byKey = packages.ToDictionary(package => package.Reference.Key, StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var key))
        {
            if (!byKey.TryGetValue(key, out var package))
                throw new ArgumentException("configuration-dependency-missing");
            if (!reachable.Add(key)) continue;
            foreach (var dependency in package.Dependencies) pending.Push(dependency);
        }
        if (reachable.Count != packages.Length)
            throw new ArgumentException("configuration-package-outside-closure");
        var contentKeys = packages.SelectMany(package => package.Content).Select(reference => reference.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (!contentKeys.SetEquals(ownership.Select(owner => owner.DefinitionKey)))
            throw new ArgumentException("configuration-ownership-incomplete");
        foreach (var owner in ownership)
        {
            Required(owner.PackageKey);
            if (!byKey.TryGetValue(owner.PackageKey, out var package)
                || !package.Content.Any(reference => reference.Key == owner.DefinitionKey))
                throw new ArgumentException("configuration-owner-content-missing");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("contract", "harborline.configuration-generation/v1");
            writer.WriteString("tenantKey", resolved.TenantKey);
            Strings(writer, "activePackageKeys", roots);
            writer.WriteStartArray("packages");
            foreach (var package in packages)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("reference");
                Reference(writer, package.Reference);
                writer.WriteStartArray("content");
                foreach (var reference in package.Content) Reference(writer, reference);
                writer.WriteEndArray();
                Strings(writer, "dependencies", package.Dependencies);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("ownership");
            foreach (var owner in ownership)
            {
                writer.WriteStartObject();
                writer.WriteString("definitionKey", owner.DefinitionKey);
                writer.WriteString("packageKey", owner.PackageKey);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("platformContract");
            Reference(writer, contract);
            writer.WriteStartArray("policies");
            foreach (var policy in policies) Reference(writer, policy);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new ConfigurationGeneration(stream.ToArray());
    }

    private static T[] Unique<T>(IEnumerable<T> values, Func<T, string> key)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in array)
        {
            ArgumentNullException.ThrowIfNull(value);
            Required(key(value));
            if (!seen.Add(key(value))) throw new ArgumentException("configuration-reference-duplicate");
        }
        return array.OrderBy(key, StringComparer.Ordinal).ToArray();
    }

    private static ConfigurationPackage Snapshot(ConfigurationPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(Validate(package.Reference), Unique(package.Content, reference => Validate(reference).Key),
            Unique(package.Dependencies, key => key));
    }

    private static ConfigurationReference Validate(ConfigurationReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Required(reference.Key);
        Required(reference.Revision);
        if (reference.Digest is not { Length: 64 } || reference.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("configuration-digest-invalid");
        return reference;
    }

    private static void Required(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("configuration-reference-required");
    }

    private static void Reference(Utf8JsonWriter writer, ConfigurationReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString("key", reference.Key);
        writer.WriteString("revision", reference.Revision);
        writer.WriteString("digest", reference.Digest);
        writer.WriteEndObject();
    }

    private static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }
}
