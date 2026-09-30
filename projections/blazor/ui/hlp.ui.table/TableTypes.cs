namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

/// <summary>How tightly table rows are packed.</summary>
public enum TableDensity
{
    /// <summary>Uses tight row padding to fit more rows.</summary>
    Small,
    /// <summary>Uses standard row padding.</summary>
    Medium
}

/// <summary>The table settings shared with its rows and cells; currently the density.</summary>
public sealed record TableContext(TableDensity Density);
