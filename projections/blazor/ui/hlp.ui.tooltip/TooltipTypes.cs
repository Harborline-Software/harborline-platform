namespace Harborline.UIAdapters.Blazor.Components.Feedback;

/// <summary>Which side of its trigger a tooltip appears on.</summary>
public enum TooltipSide
{
    /// <summary>Places the tooltip above its anchor.</summary>
    Top,
    /// <summary>Places the tooltip to the right of its anchor.</summary>
    Right,
    /// <summary>Places the tooltip below its anchor.</summary>
    Bottom,
    /// <summary>Places the tooltip to the left of its anchor.</summary>
    Left
}

/// <summary>The state a tooltip shares with its trigger and content.</summary>
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

/// <summary>Whether the tooltip is showing.</summary>
    public bool Open => getOpen();
/// <summary>Id of the tooltip content element, referenced by the trigger for assistive technology.</summary>
    public string ContentId { get; }
/// <summary>Marks pointer hover on the trigger as started or ended.</summary>
    public Task SetHoverAsync(bool active) => setOwnership(active, false);
/// <summary>Marks keyboard focus on the trigger as started or ended.</summary>
    public Task SetFocusAsync(bool active) => setOwnership(active, true);
/// <summary>Hides the tooltip, for example on Escape.</summary>
    public Task DismissAsync() => dismiss();
}
