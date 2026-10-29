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

    // T-1123: the movable header is a named group and the keyboard resize handles are one per axis, height then
    // width, in both lanes. The pointer corner is not a tab stop. The caller supplies every label; nothing falls back.
    [Fact]
    public void HeaderIsANamedGroupAndKeyboardHandlesAreOnePerAxis()
    {
        var cut = Render<HarborlineWindow>(p => { Labels(p); p.Add(x => x.MoveLabel, "نقل النافذة").Add(x => x.MoveDescriptionLabel, "استخدم مفاتيح الأسهم"); });
        var header = cut.Find(".hl-window__title-bar");
        Assert.Equal("group", header.GetAttribute("role"));
        Assert.Equal("نقل النافذة", header.GetAttribute("aria-label"));
        Assert.Equal("استخدم مفاتيح الأسهم", header.GetAttribute("aria-description"));
        Assert.Equal("0", header.GetAttribute("tabindex"));
        var stops = cut.FindAll(".hl-window__resize[tabindex='0']");
        Assert.Equal(["s", "e"], stops.Select(handle => handle.GetAttribute("data-resize-edge")));
        Assert.Equal(["Resize height", "Resize width"], stops.Select(handle => handle.GetAttribute("aria-label")));
        Assert.All(stops, handle => Assert.Equal("separator", handle.GetAttribute("role")));
        var corner = cut.Find("[data-resize-edge=se]");
        Assert.Equal("true", corner.GetAttribute("aria-hidden"));
        Assert.Null(corner.GetAttribute("tabindex"));
        Assert.Null(corner.GetAttribute("role"));
    }

    // T-1123: a movable window has no built-in move label or description; a missing one is a contract error.
    [Theory]
    [InlineData(null, "Utilisez les flèches")]
    [InlineData("Déplacer la fenêtre", null)]
    [InlineData(" ", "Utilisez les flèches")]
    public void MovableWindowWithoutCallerMoveLabelsIsRejected(string? moveLabel, string? moveDescription)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<HarborlineWindow>(p =>
        {
            Labels(p);
            if (moveLabel is not null) p.Add(x => x.MoveLabel, moveLabel);
            if (moveDescription is not null) p.Add(x => x.MoveDescriptionLabel, moveDescription);
        }));
        Assert.Equal("localized-control-labels-required", error.Message);
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

    [Theory, Trait("ModuleConformance", "hlp.ui.window")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.window", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
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
        });
    }

    private static string[] Classes(System.Text.Json.JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();

    internal IRenderedComponent<HarborlineWindow> RenderWindow(Action<ComponentParameterCollectionBuilder<HarborlineWindow>>? configure = null)
    {
        return Render<HarborlineWindow>(p =>
        {
            Labels(p);
            p.Add(x => x.MoveLabel, "Move window").Add(x => x.MoveDescriptionLabel, "Press arrow keys to move window");
            configure?.Invoke(p);
        });
    }

    private static void Labels(ComponentParameterCollectionBuilder<HarborlineWindow> p) =>
        p.Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "Inspector")))
         .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body")))
         .Add(x => x.CloseLabel, "Close").Add(x => x.MinimizeLabel, "Minimize")
         .Add(x => x.MaximizeLabel, "Maximize").Add(x => x.RestoreLabel, "Restore")
         .Add(x => x.ResizeWidthLabel, "Resize width").Add(x => x.ResizeHeightLabel, "Resize height")
         .Add(x => x.ResizeBothLabel, "Resize width and height");
}
