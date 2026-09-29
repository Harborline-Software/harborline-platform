namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

/// <summary>Base type for the data a chart draws.</summary>
public abstract record ChartDefinition;

/// <summary>One line of a line chart: its name and a value per category, with gaps for missing values.</summary>
public sealed record LineChartSeries(string Name, IReadOnlyList<double?> Values);

/// <summary>Data for a line chart: the category labels along the axis and the series to plot.</summary>
public sealed record LineChartDefinition(
    IReadOnlyList<string> Categories,
    IReadOnlyList<LineChartSeries> Series) : ChartDefinition;

/// <summary>One slice of a donut chart: its label and value, empty when there is no value.</summary>
public sealed record DonutChartSlice(string Label, double? Value);

/// <summary>Data for a donut chart: the slices to draw.</summary>
public sealed record DonutChartDefinition(
    IReadOnlyList<DonutChartSlice> Slices) : ChartDefinition;

/// <summary>Whether chart animation follows the host or is turned off.</summary>
public enum ChartMotion
{
/// <summary>Defers to the host motion preference for chart animation.</summary>
    Host,
/// <summary>Turns off chart animation.</summary>
    Disabled
}
