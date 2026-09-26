namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A released surface named by a provider-neutral surface catalogue.</summary>
/// <param name="Id">The stable surface identity.</param>
/// <param name="Label">The reader-facing surface label.</param>
public sealed record ReleasedSurface(string Id, string Label);

/// <summary>The released catalogue from which navigation resolves surfaces.</summary>
/// <param name="Id">The stable catalogue identity.</param>
/// <param name="Surfaces">The surfaces this catalogue exposes.</param>
public sealed record ReleasedSurfaceCatalogue(string Id, IReadOnlyList<ReleasedSurface> Surfaces);

/// <summary>A released navigation entry that selects one released editor destination.</summary>
/// <param name="Id">The stable navigation identity.</param>
/// <param name="Label">The reader-facing navigation label.</param>
/// <param name="EditorDestinationId">The released editor destination selected by this entry.</param>
public sealed record ReleasedNavigationEntry(string Id, string Label, string EditorDestinationId);

/// <summary>A released editor destination that selects one surface from the catalogue.</summary>
/// <param name="Id">The stable destination identity.</param>
/// <param name="SurfaceId">The released surface identity.</param>
public sealed record ReleasedEditorDestination(string Id, string SurfaceId);

/// <summary>
/// DES-0052 layout-eng-24's single provider-neutral definition. It keeps the surface catalogue,
/// navigation entries, and editor destinations in one released document so a consumer has no
/// compiled destination to substitute when an installed definition is unavailable.
/// </summary>
/// <param name="Identity">The stable released definition identity.</param>
/// <param name="Version">The immutable semantic revision.</param>
/// <param name="SurfaceCatalogue">The released surface catalogue.</param>
/// <param name="NavigationEntries">The released navigation entries.</param>
/// <param name="EditorDestinations">The released editor destinations.</param>
public sealed record ReleasedNavigationDefinition(
    string Identity,
    string Version,
    ReleasedSurfaceCatalogue SurfaceCatalogue,
    IReadOnlyList<ReleasedNavigationEntry> NavigationEntries,
    IReadOnlyList<ReleasedEditorDestination> EditorDestinations);
