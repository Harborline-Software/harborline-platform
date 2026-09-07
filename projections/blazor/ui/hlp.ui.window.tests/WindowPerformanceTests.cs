using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class WindowPerformanceTests : BunitContext
{
    public WindowPerformanceTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void NinetySixUpdatesAndTwoHundredFiftySixNodesKeepLatestState()
    {
        var cut = RenderWindow(0);
        for (var cycle = 0; cycle < 96; cycle++)
        {
            cut.Render(Parameters(cycle));
            Assert.Equal(256, cut.FindAll(".payload").Count);
            Assert.Equal($"{cycle}-255", cut.Find(".payload:last-child").TextContent);
        }
    }

    private IRenderedComponent<HarborlineWindow> RenderWindow(int cycle) => Render<HarborlineWindow>(Parameters(cycle));
    private static Action<ComponentParameterCollectionBuilder<HarborlineWindow>> Parameters(int cycle) => p => p
        .Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, $"Inspector {cycle}")))
        .Add(x => x.ChildContent, Nodes(cycle)).Add(x => x.Width, 400 + cycle).Add(x => x.Height, 300 + cycle)
        .Add(x => x.CloseLabel, "Close").Add(x => x.MinimizeLabel, "Minimize").Add(x => x.MaximizeLabel, "Maximize")
        .Add(x => x.RestoreLabel, "Restore").Add(x => x.ResizeWidthLabel, "Resize width")
        .Add(x => x.ResizeHeightLabel, "Resize height").Add(x => x.ResizeBothLabel, "Resize width and height");
    private static RenderFragment Nodes(int cycle) => builder =>
    {
        for (var index = 0; index < 256; index++)
        {
            builder.OpenElement(index * 3, "span"); builder.AddAttribute(index * 3 + 1, "class", "payload"); builder.AddContent(index * 3 + 2, $"{cycle}-{index}"); builder.CloseElement();
        }
    };
}
