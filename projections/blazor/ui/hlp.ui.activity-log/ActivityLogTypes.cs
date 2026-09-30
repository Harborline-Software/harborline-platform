namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>Whether activity entries render as a list or a table.</summary>
public enum ActivityLogLayout
{
    /// <summary>Renders entries as a stacked list.</summary>
    List,
    /// <summary>Renders entries as rows in a table.</summary>
    Table
}
/// <summary>The visual tone of an activity entry, from neutral to destructive.</summary>
public enum ActivityTone
{
    /// <summary>Styles the entry with no emphasis.</summary>
    Neutral,
    /// <summary>Styles the entry as a positive event.</summary>
    Positive,
    /// <summary>Styles the entry as a warning that needs attention.</summary>
    Warning,
    /// <summary>Styles the entry as a failure or destructive event.</summary>
    Danger,
    /// <summary>Styles the entry as informational.</summary>
    Info
}
/// <summary>One row in an activity log: who did what and when, with optional detail, tone and category.</summary>
public sealed record ActivityEntry(string Id,string Actor,string Action,string Timestamp,string? MachineTimestamp=null,string? Detail=null,ActivityTone Tone=ActivityTone.Neutral,string? Category=null);
