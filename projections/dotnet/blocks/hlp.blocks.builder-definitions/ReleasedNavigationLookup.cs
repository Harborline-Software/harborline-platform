using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Supplies the reader's authorization decision for one resolved released navigation path.</summary>
public interface IReleasedNavigationAccess
{
    /// <summary>Returns whether the reader may open the complete resolved definition path.</summary>
    bool CanRead(
        ReleasedNavigationDefinition definition,
        ReleasedNavigationEntry entry,
        ReleasedEditorDestination destination,
        ReleasedSurface surface);
}

/// <summary>The resolved released navigation path, with no compiled destination fallback.</summary>
/// <param name="Definition">The installed definition that supplied the path.</param>
/// <param name="Entry">The requested released navigation entry.</param>
/// <param name="Destination">The released editor destination selected by the entry.</param>
/// <param name="Surface">The released catalogue surface selected by the destination.</param>
public sealed record ResolvedReleasedNavigation(
    ReleasedNavigationDefinition Definition,
    ReleasedNavigationEntry Entry,
    ReleasedEditorDestination Destination,
    ReleasedSurface Surface);

/// <summary>
/// An installed, admitted released-navigation collection. Lookup has only installed definitions as
/// input; it never provides a compiled navigation or editor destination when a definition is absent.
/// </summary>
public sealed class ReleasedNavigationLookup
{
    private readonly IReadOnlyDictionary<string, ResolvedReleasedNavigation> paths;

    private ReleasedNavigationLookup(IReadOnlyDictionary<string, ResolvedReleasedNavigation> paths)
        => this.paths = paths;

    /// <summary>Admits and indexes exported definitions before any reader may resolve them.</summary>
    public static ReleasedNavigationLookup Install(IEnumerable<ReleasedNavigationDefinitionPackageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var paths = new Dictionary<string, ResolvedReleasedNavigation>(StringComparer.Ordinal);
        var refusals = new List<DefinitionRefusal>();
        foreach (var entry in entries)
        {
            var target = $"{entry.DefinitionId}@{entry.Version}";
            ReleasedNavigationDefinition definition;
            try
            {
                definition = ReleasedNavigationDefinitionJson.Deserialize(entry.Content.Payload.Span);
                ReleasedNavigationDefinitionAdmission.Validate(definition, DefinitionAdmissionPhase.Install);
            }
            catch (Exception exception) when (exception is JsonException or DefinitionRefusalException)
            {
                if (exception is DefinitionRefusalException refused)
                    refusals.AddRange(refused.Refusals.Select(refusal => refusal with { Target = target }));
                else
                    refusals.Add(new(ReleasedNavigationDefinitionCodes.DefinitionInvalid, "/", target));
                continue;
            }

            foreach (var navigationEntry in definition.NavigationEntries)
            {
                var destination = definition.EditorDestinations.Single(value => value.Id == navigationEntry.EditorDestinationId);
                var surface = definition.SurfaceCatalogue.Surfaces.Single(value => value.Id == destination.SurfaceId);
                if (!paths.TryAdd(navigationEntry.Id, new(definition, navigationEntry, destination, surface)))
                    refusals.Add(new(ReleasedNavigationDefinitionCodes.MemberInvalid, $"/navigation_entries/{navigationEntry.Id}", target));
            }
        }
        if (refusals.Count > 0) throw new DefinitionRefusalException(DefinitionAdmissionPhase.Install, refusals);
        return new ReleasedNavigationLookup(paths);
    }

    /// <summary>Resolves one installed entry and refuses visibly when it is missing or unreadable.</summary>
    public ResolvedReleasedNavigation Resolve(string navigationEntryId, IReleasedNavigationAccess access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationEntryId);
        ArgumentNullException.ThrowIfNull(access);
        if (!paths.TryGetValue(navigationEntryId, out var path))
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Render,
                [new(ReleasedNavigationDefinitionCodes.NavigationEntryMissing, $"/navigation_entries/{navigationEntryId}")]);
        if (!access.CanRead(path.Definition, path.Entry, path.Destination, path.Surface))
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Render,
                [new(ReleasedNavigationDefinitionCodes.DefinitionUnreadable, $"/navigation_entries/{navigationEntryId}")]);
        return path;
    }
}
