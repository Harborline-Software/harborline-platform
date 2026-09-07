namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public abstract record ChartDefinition;

public sealed record LineChartSeries(string Name, IReadOnlyList<double?> Values);

public sealed record LineChartDefinition(
    IReadOnlyList<string> Categories,
    IReadOnlyList<LineChartSeries> Series) : ChartDefinition;

public sealed record DonutChartSlice(string Label, double? Value);

public sealed record DonutChartDefinition(
    IReadOnlyList<DonutChartSlice> Slices) : ChartDefinition;

public enum ChartMotion
{
    Host,
    Disabled
}
