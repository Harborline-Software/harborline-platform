namespace Harborline.UIAdapters.Blazor.Components.Buttons;
/// <summary>The file format a data export produces.</summary>
public enum ExportFormat
{
    /// <summary>Exports the data as comma-separated values.</summary>
    Csv,
    /// <summary>Exports the data as an Excel workbook.</summary>
    Xlsx,
    /// <summary>Exports the data as a PDF document.</summary>
    Pdf,
    /// <summary>Exports the data as JSON.</summary>
    Json,
    /// <summary>Exports the data as a Markdown table.</summary>
    Md
}
