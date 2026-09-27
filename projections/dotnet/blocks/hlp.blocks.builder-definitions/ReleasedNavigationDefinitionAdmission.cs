namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Stable refusal codes for released navigation definition admission and lookup.</summary>
public static class ReleasedNavigationDefinitionCodes
{
    /// <summary>The definition identity, version, or catalogue is absent.</summary>
    public const string DefinitionInvalid = "released-navigation.definition.invalid";
    /// <summary>A catalogue surface, navigation entry, or editor destination is absent or duplicated.</summary>
    public const string MemberInvalid = "released-navigation.member.invalid";
    /// <summary>A navigation entry names no released editor destination.</summary>
    public const string EditorDestinationMissing = "released-navigation.editor-destination.missing";
    /// <summary>An editor destination names no released catalogue surface.</summary>
    public const string SurfaceMissing = "released-navigation.surface.missing";
    /// <summary>An installed definition does not contain the requested navigation entry.</summary>
    public const string NavigationEntryMissing = "released-navigation.navigation-entry.missing";
    /// <summary>The reader cannot read the resolved released definition.</summary>
    public const string DefinitionUnreadable = "released-navigation.definition.unreadable";
}

/// <summary>Admits the one released-navigation document before it is exported or installed.</summary>
public static class ReleasedNavigationDefinitionAdmission
{
    /// <summary>Validates a definition at the caller-stated lifecycle stage.</summary>
    public static void Validate(ReleasedNavigationDefinition definition, DefinitionAdmissionPhase stage)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var refusals = new List<DefinitionRefusal>();
        if (string.IsNullOrWhiteSpace(definition.Identity) || !LayoutVersionSyntax.IsValid(definition.Version)
            || definition.SurfaceCatalogue is null || string.IsNullOrWhiteSpace(definition.SurfaceCatalogue.Id))
            refusals.Add(new(ReleasedNavigationDefinitionCodes.DefinitionInvalid, "/"));

        var surfaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (surface, index) in (definition.SurfaceCatalogue?.Surfaces ?? []).Select((value, index) => (value, index)))
            if (surface is null || string.IsNullOrWhiteSpace(surface.Id) || string.IsNullOrWhiteSpace(surface.Label) || !surfaces.Add(surface.Id))
                refusals.Add(new(ReleasedNavigationDefinitionCodes.MemberInvalid, $"/surface_catalogue/surfaces/{index}"));

        var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (destination, index) in (definition.EditorDestinations ?? []).Select((value, index) => (value, index)))
        {
            var pointer = $"/editor_destinations/{index}";
            if (destination is null || string.IsNullOrWhiteSpace(destination.Id) || !destinations.Add(destination.Id))
                refusals.Add(new(ReleasedNavigationDefinitionCodes.MemberInvalid, pointer));
            else if (string.IsNullOrWhiteSpace(destination.SurfaceId) || !surfaces.Contains(destination.SurfaceId))
                refusals.Add(new(ReleasedNavigationDefinitionCodes.SurfaceMissing, $"{pointer}/surface_id"));
        }

        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, index) in (definition.NavigationEntries ?? []).Select((value, index) => (value, index)))
        {
            var pointer = $"/navigation_entries/{index}";
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Label) || !entries.Add(entry.Id))
                refusals.Add(new(ReleasedNavigationDefinitionCodes.MemberInvalid, pointer));
            else if (string.IsNullOrWhiteSpace(entry.EditorDestinationId) || !destinations.Contains(entry.EditorDestinationId))
                refusals.Add(new(ReleasedNavigationDefinitionCodes.EditorDestinationMissing, $"{pointer}/editor_destination_id"));
        }

        if (refusals.Count > 0) throw new DefinitionRefusalException(stage, refusals);
    }
}
