using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Enumerates the supported accordion expansion behaviors.</summary>
public enum AccordionMode
{
    /// <summary>Keeps at most one panel open, closing the others when one opens.</summary>
    Single,
    /// <summary>Lets several panels stay open at the same time.</summary>
    Multiple
}

/// <summary>One collapsible accordion section: its id, heading, body content and whether it is disabled.</summary>
public sealed record HarborlineAccordionItem(
    string Id,
    RenderFragment Heading,
    RenderFragment Content,
    bool Disabled = false);
