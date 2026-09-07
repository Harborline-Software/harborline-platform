namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public enum TableDensity { Small, Medium }

public sealed record TableContext(TableDensity Density);
