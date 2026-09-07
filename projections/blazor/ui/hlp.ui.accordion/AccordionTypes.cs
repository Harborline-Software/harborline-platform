using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public enum AccordionMode { Single, Multiple }

public sealed record HarborlineAccordionItem(
    string Id,
    RenderFragment Heading,
    RenderFragment Content,
    bool Disabled = false);
