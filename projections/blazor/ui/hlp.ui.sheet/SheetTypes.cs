using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public enum SheetSide { Top, Right, Bottom, Left }

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

    public bool Open => getOpen();
    public bool Modal { get; internal set; }
    public string TriggerId { get; }
    public string ContentId { get; }
    public string TitleId { get; }
    public string DescriptionId { get; }
    internal ElementReference TriggerElement { get; set; }
    public Task SetOpenAsync(bool open) => setOpen(open);
}
