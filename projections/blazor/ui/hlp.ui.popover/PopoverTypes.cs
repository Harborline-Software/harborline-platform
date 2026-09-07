using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Feedback;

public enum PopoverSide { Top, Right, Bottom, Left }
public enum PopoverAlign { Start, Center, End }

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

    public bool Open => getOpen();
    public string TriggerId { get; }
    public string ContentId { get; }
    internal ElementReference TriggerElement { get; set; }
    internal ElementReference AnchorElement { get; set; }
    internal bool HasAnchor { get; set; }
    public Task SetOpenAsync(bool open) => setOpen(open);
}
