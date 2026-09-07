using System.Text.RegularExpressions;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The pinned advisory key-suggestion algorithm shared by the Forms and Workflows builders.
/// Port of apps/carrier/src/definitions/slug.ts:13-36 at 3410883405996dc8e4871418a441f52970d9994e.
/// </summary>
public static partial class DefinitionKeySuggester
{
    /// <summary>
    /// Suggests a definition key from a display name: lowercase ASCII kebab slug, "untitled"
    /// fallback, ".v1" suffix, and "-2"/"-3"… collision sequencing against
    /// <paramref name="existingKeys"/> using exact-case membership, exactly as pinned.
    /// </summary>
    /// <param name="name">The user-typed display name the slug derives from.</param>
    /// <param name="existingKeys">The tenant's current key set for collision sequencing.</param>
    /// <returns>The first available suggested key.</returns>
    public static string Suggest(string name, IReadOnlySet<string> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(existingKeys);
        var slug = NonAsciiAlphaNumeric().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        var basis = slug.Length == 0 ? "untitled" : slug;
        var candidate = $"{basis}.v1";
        for (var suffix = 2; existingKeys.Contains(candidate); suffix++)
            candidate = $"{basis}-{suffix}.v1";
        return candidate;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAsciiAlphaNumeric();
}

/// <summary>A request to resolve the definitive key for a new definition.</summary>
/// <param name="Tenant">The tenant whose key namespace is being allocated into.</param>
/// <param name="Kind">The definition kind whose namespace the key lives in.</param>
/// <param name="DisplayName">The user-typed display name.</param>
/// <param name="SuggestedKey">The client's advisory suggestion, when it made one.</param>
public sealed record DefinitionKeyRequest(string Tenant, DefinitionKind Kind, string DisplayName, string? SuggestedKey);

/// <summary>The definitive key granted for a definition.</summary>
/// <param name="Key">The granted key.</param>
public sealed record DefinitionKeyResolution(string Key);

/// <summary>The deliberately thin policy seam for key allocation. Ruled 2026-08-17 (migration ticket 090): UI-suggested with server-side fail-closed enforcement binds here when the builder-contract wave lands; this wave ships the seam only.</summary>
public interface IDefinitionKeyAuthority
{
    /// <summary>Resolves the definitive key for <paramref name="request"/> under the bound allocation policy.</summary>
    /// <param name="request">The allocation request.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    ValueTask<DefinitionKeyResolution> ResolveAsync(DefinitionKeyRequest request, CancellationToken cancellationToken = default);
}
