using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class WindowTests : BunitContext
{
    public WindowTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void DefaultsActionsAndControlledStateRemainSemantic()
    {
        var states = new List<HarborlineWindowState>();
        var cut = RenderWindow(p => p.Add(x => x.State, HarborlineWindowState.Default).Add(x => x.StateChanged, value => states.Add(value)));
        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("default", dialog.GetAttribute("data-state"));
        Assert.Contains("width:400px", dialog.GetAttribute("style"));
        cut.Find("button[aria-label='Minimize']").Click();
        Assert.Equal("default", cut.Find("[role=dialog]").GetAttribute("data-state"));
        Assert.Equal([HarborlineWindowState.Minimized], states);
    }

    [Fact]
    public void KeyboardMoveResizeAndEscapeUsePinnedSteps()
    {
        var moves = new List<WindowPosition>();
        var sizes = new List<WindowSize>();
        var closed = 0;
        var cut = RenderWindow(p => p.Add(x => x.Moved, value => moves.Add(value)).Add(x => x.Resized, value => sizes.Add(value)).Add(x => x.Closed, () => closed++));
        cut.Find(".hl-window__title-bar").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        cut.Find("[data-resize-edge=e]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(new WindowPosition(80, 130), moves.Single());
        Assert.Equal(new WindowSize(390, 300), sizes.Single());
        Assert.Equal(1, closed);
    }

    [Fact]
    public void MaximizedEscapeRestoresBeforeCloseAndModalOverlayTracksMinimize()
    {
        var states = new List<HarborlineWindowState>();
        var cut = RenderWindow(p => p.Add(x => x.DefaultState, HarborlineWindowState.Maximized).Add(x => x.Modal, true).Add(x => x.StateChanged, value => states.Add(value)));
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal([HarborlineWindowState.Default], states);
        Assert.Equal("default", cut.Find("[role=dialog]").GetAttribute("data-state"));
        Assert.Single(cut.FindAll("[data-hl-window-overlay]"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.window")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("window.", id);
        Assert.Equal("default", RenderWindow().Find("[role=dialog]").GetAttribute("data-state"));

        if (id != "window.class-parity") return;
        var input = fixture.RootElement.GetProperty("input");
        var expected = fixture.RootElement.GetProperty("expected");
        foreach (var state in input.GetProperty("states").EnumerateArray().Select(value => value.GetString()!))
        {
            var cut = RenderWindow(p => p
                .Add(x => x.State, Enum.Parse<HarborlineWindowState>(state, ignoreCase: true))
                .Add(x => x.Modal, input.GetProperty("modal").GetBoolean())
                .Add(x => x.Resizable, input.GetProperty("resizable").GetBoolean())
                .Add(x => x.Draggable, input.GetProperty("draggable").GetBoolean()));
            var dialog = cut.Find("[role=dialog]");
            Assert.Equal(Classes(expected.GetProperty("rootClasses"), state), dialog.ClassList);
            var style = dialog.GetAttribute("style") ?? string.Empty;
            Assert.Contains($"position:{expected.GetProperty("inlinePosition").GetString()}", style);
            Assert.Contains($"z-index:{expected.GetProperty("inlineZIndex").GetString()}", style);
            if (state == "default")
            {
                Assert.Equal(Classes(expected, "resizeWidthClasses"), cut.Find("[data-resize-edge=e]").ClassList);
                Assert.Equal(Classes(expected, "resizeHeightClasses"), cut.Find("[data-resize-edge=s]").ClassList);
                Assert.Equal(Classes(expected, "resizeCornerClasses"), cut.Find("[data-resize-edge=se]").ClassList);
            }

            // This lane used to wrap its portal in hl-window__portal and to spell the resize handles
            // --east/--south; the authority never defined any of the three.
            foreach (var absent in expected.GetProperty("absentClasses").EnumerateArray())
            {
                Assert.Empty(cut.FindAll($".{absent.GetString()}"));
            }
        }
    }

    private static string[] Classes(System.Text.Json.JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

    internal IRenderedComponent<HarborlineWindow> RenderWindow(Action<ComponentParameterCollectionBuilder<HarborlineWindow>>? configure = null)
    {
        return Render<HarborlineWindow>(p =>
        {
            p.Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "Inspector")))
             .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body")))
             .Add(x => x.CloseLabel, "Close").Add(x => x.MinimizeLabel, "Minimize")
             .Add(x => x.MaximizeLabel, "Maximize").Add(x => x.RestoreLabel, "Restore")
             .Add(x => x.ResizeWidthLabel, "Resize width").Add(x => x.ResizeHeightLabel, "Resize height")
             .Add(x => x.ResizeBothLabel, "Resize width and height");
            configure?.Invoke(p);
        });
    }
}
