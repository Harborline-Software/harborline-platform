namespace Harborline.UIAdapters.Blazor.Components.Navigation;

public enum ActivityLogLayout { List, Table }
public enum ActivityTone { Neutral, Positive, Warning, Danger, Info }
public sealed record ActivityEntry(string Id,string Actor,string Action,string Timestamp,string? MachineTimestamp=null,string? Detail=null,ActivityTone Tone=ActivityTone.Neutral,string? Category=null);
