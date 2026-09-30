using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Feedback;

/// <summary>Which side of its trigger a popover opens on.</summary>
public enum PopoverSide
{
    /// <summary>Places the popover above its anchor.</summary>
    Top,
    /// <summary>Places the popover to the right of its anchor.</summary>
    Right,
    /// <summary>Places the popover below its anchor.</summary>
    Bottom,
    /// <summary>Places the popover to the left of its anchor.</summary>
    Left
}
/// <summary>How a popover aligns along its trigger's edge.</summary>
public enum PopoverAlign
{
    /// <summary>Aligns the popover with the start edge of its trigger.</summary>
    Start,
    /// <summary>Centers the popover on its trigger.</summary>
    Center,
    /// <summary>Aligns the popover with the end edge of its trigger.</summary>
    End
}

/// <summary>Shared composition state for the bounded popover component family.</summary>
public sealed class PopoverContext
{
    private readonly Func<bool> getOpen;
    private readonly Func<bool, Task> setOpen;

    internal PopoverContext(Func<bool> getOpen, Func<bool, Task> setOpen, string triggerId, string contentId)
    {
        this.getOpen = getOpen;
        this.setOpen = setOpen;
        TriggerId = triggerId;
        ContentId = contentId;
    }

    /// <summary>Whether the popover is open.</summary>
    public bool Open => getOpen();
    /// <summary>Id of the element that opens the popover.</summary>
    public string TriggerId { get; }
    /// <summary>Id of the popover content element.</summary>
    public string ContentId { get; }
    internal ElementReference TriggerElement { get; set; }
    internal ElementReference AnchorElement { get; set; }
    internal bool HasAnchor { get; set; }
    /// <summary>Opens or closes the popover.</summary>
    public Task SetOpenAsync(bool open) => setOpen(open);
}
