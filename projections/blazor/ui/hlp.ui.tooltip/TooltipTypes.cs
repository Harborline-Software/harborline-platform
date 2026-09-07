namespace Harborline.UIAdapters.Blazor.Components.Feedback;

public enum TooltipSide { Top, Right, Bottom, Left }

public sealed class TooltipContext
{
    private readonly Func<bool> getOpen;
    private readonly Func<bool, bool, Task> setOwnership;
    private readonly Func<Task> dismiss;

    internal TooltipContext(Func<bool> getOpen, Func<bool, bool, Task> setOwnership, Func<Task> dismiss, string contentId)
    {
        this.getOpen = getOpen;
        this.setOwnership = setOwnership;
        this.dismiss = dismiss;
        ContentId = contentId;
    }

    public bool Open => getOpen();
    public string ContentId { get; }
    public Task SetHoverAsync(bool active) => setOwnership(active, false);
    public Task SetFocusAsync(bool active) => setOwnership(active, true);
    public Task DismissAsync() => dismiss();
}
