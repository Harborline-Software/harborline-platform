using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Which screen edge a sheet slides in from.</summary>
public enum SheetSide
{
    /// <summary>Slides the sheet in from the top edge of the screen.</summary>
    Top,
    /// <summary>Slides the sheet in from the right edge of the screen.</summary>
    Right,
    /// <summary>Slides the sheet in from the bottom edge of the screen.</summary>
    Bottom,
    /// <summary>Slides the sheet in from the left edge of the screen.</summary>
    Left
}

/// <summary>The state a sheet shares with its trigger, content and close controls.</summary>
public sealed class SheetContext
{
    private readonly Func<bool> getOpen;
    private readonly Func<bool, Task> setOpen;

    internal SheetContext(Func<bool> getOpen, Func<bool, Task> setOpen, bool modal, string prefix)
    {
        this.getOpen = getOpen;
        this.setOpen = setOpen;
        Modal = modal;
        TriggerId = $"{prefix}-trigger";
        ContentId = $"{prefix}-content";
        TitleId = $"{prefix}-title";
        DescriptionId = $"{prefix}-description";
    }

    /// <summary>Whether the sheet is open.</summary>
    public bool Open => getOpen();
    /// <summary>Whether the sheet is modal and blocks the page behind it.</summary>
    public bool Modal { get; internal set; }
    /// <summary>Id of the element that opens the sheet.</summary>
    public string TriggerId { get; }
    /// <summary>Id of the sheet content element.</summary>
    public string ContentId { get; }
    /// <summary>Id of the sheet title, which labels it for assistive technology.</summary>
    public string TitleId { get; }
    /// <summary>Id of the sheet description, read by assistive technology.</summary>
    public string DescriptionId { get; }
    internal ElementReference TriggerElement { get; set; }
    /// <summary>Opens or closes the sheet.</summary>
    public Task SetOpenAsync(bool open) => setOpen(open);
}
