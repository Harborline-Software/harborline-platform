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
        Assert.Equal("Inspector", Accname.AccessibleName(dialog));
        Accname.AssertIdReferencesResolve(cut);
        Control(cut, "Minimize").Click();
        Assert.Equal("default", cut.Find("[role=dialog]").GetAttribute("data-state"));
        Assert.Equal([HarborlineWindowState.Minimized], states);
    }

    [Fact]
    public void TheTitleNamesTheDialogAndEachControlCarriesItsLocalizedLabel()
    {
        // Labels no fallback would produce, so a hard-coded English aria-label cannot pass
        // (interface.yaml: controls "expose caller-localized accessible labels").
        var cut = RenderFrenchWindow(p => p.Add(x => x.AccessibleLabel, "Panneau inspecteur"));
        var dialog = cut.Find("[role=dialog]");
        Accname.AssertIdReferencesResolve(cut);
        Assert.Equal("Inspecteur", Accname.AccessibleName(dialog));
        Accname.AssertReferences(dialog, "aria-labelledby", cut.Find(".hl-window__title"));
        // interface.yaml: "The dialog is labelled by title or an explicit localized fallback"; with a title the
        // fallback is not emitted beside it (accname 1.2 §4.3.2 takes 2B aria-labelledby before 2D aria-label).
        Assert.Null(dialog.GetAttribute("aria-label"));
        Assert.Equal(["Réduire", "Agrandir", "Fermer"], cut.FindAll("button.hl-window__control").Select(Accname.AccessibleName));
        Assert.Equal(["Largeur", "Hauteur", "Les deux"], cut.FindAll("[role=separator]").Select(Accname.AccessibleName));
    }

    [Fact]
    public void AnUntitledWindowIsNamedByItsAccessibleLabel()
    {
        var cut = Render<HarborlineWindow>(p => FrenchLabels(p).Add(x => x.AccessibleLabel, "Panneau inspecteur"));
        Accname.AssertIdReferencesResolve(cut);
        Assert.Equal("Panneau inspecteur", Accname.AccessibleName(cut.Find("[role=dialog]")));
    }

    [Theory]
    [InlineData(HarborlineWindowState.Default, "Réduire", "Agrandir")]
    [InlineData(HarborlineWindowState.Minimized, "Restaurer", "Agrandir")]
    [InlineData(HarborlineWindowState.Maximized, "Réduire", "Restaurer")]
    public void StateControlsAreNamedForWhatTheyWillDo(HarborlineWindowState state, string minimize, string maximize)
    {
        var cut = RenderFrenchWindow(p => p.Add(x => x.State, state));
        Accname.AssertIdReferencesResolve(cut);
        Assert.Equal("Inspecteur", Accname.AccessibleName(cut.Find("[role=dialog]")));
        Assert.Equal([minimize, maximize, "Fermer"], cut.FindAll("button.hl-window__control").Select(Accname.AccessibleName));
    }

    [Fact]
    public void TwoWindowsOnOnePageKeepTheirIdsApart()
    {
        string[] titles = ["Inspecteur", "Console"];
        var cut = Render(builder =>
        {
            foreach (var title in titles)
            {
                builder.OpenComponent<HarborlineWindow>(0);
                builder.AddAttribute(1, nameof(HarborlineWindow.Title), (RenderFragment)(b => b.AddContent(0, title)));
                builder.AddAttribute(2, nameof(HarborlineWindow.CloseLabel), "Fermer");
                builder.AddAttribute(3, nameof(HarborlineWindow.MinimizeLabel), "Réduire");
                builder.AddAttribute(4, nameof(HarborlineWindow.MaximizeLabel), "Agrandir");
                builder.AddAttribute(5, nameof(HarborlineWindow.RestoreLabel), "Restaurer");
                builder.AddAttribute(6, nameof(HarborlineWindow.ResizeWidthLabel), "Largeur");
                builder.AddAttribute(7, nameof(HarborlineWindow.ResizeHeightLabel), "Hauteur");
                builder.AddAttribute(8, nameof(HarborlineWindow.ResizeBothLabel), "Les deux");
                builder.CloseComponent();
            }
        });
        Accname.AssertIdReferencesResolve(cut);
        Assert.Equal(["Inspecteur", "Console"], cut.FindAll("[role=dialog]").Select(Accname.AccessibleName));
    }

    private IRenderedComponent<HarborlineWindow> RenderFrenchWindow(Action<ComponentParameterCollectionBuilder<HarborlineWindow>>? configure = null) =>
        Render<HarborlineWindow>(p =>
        {
            FrenchLabels(p).Add(x => x.Title, (RenderFragment)(b => b.AddContent(0, "Inspecteur")))
                .Add(x => x.ChildContent, (RenderFragment)(b => b.AddContent(0, "Corps")));
            configure?.Invoke(p);
        });

    private static ComponentParameterCollectionBuilder<HarborlineWindow> FrenchLabels(ComponentParameterCollectionBuilder<HarborlineWindow> p) => p
        .Add(x => x.CloseLabel, "Fermer").Add(x => x.MinimizeLabel, "Réduire")
        .Add(x => x.MaximizeLabel, "Agrandir").Add(x => x.RestoreLabel, "Restaurer")
        .Add(x => x.ResizeWidthLabel, "Largeur").Add(x => x.ResizeHeightLabel, "Hauteur")
        .Add(x => x.ResizeBothLabel, "Les deux");

    private static AngleSharp.Dom.IElement Control(IRenderedComponent<HarborlineWindow> cut, string name) =>
        Assert.Single(cut.FindAll("button.hl-window__control"), button => Accname.AccessibleName(button) == name);

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
