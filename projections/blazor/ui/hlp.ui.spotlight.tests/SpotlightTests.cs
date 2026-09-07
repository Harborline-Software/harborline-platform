using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SpotlightTests : BunitContext
{
    public SpotlightTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void ComboboxUsesCallerOrderAndKeyboardSelectionClamps()
    {
        var selected = new List<string>();
        var states = new List<bool>();
        var items = new[]
        {
            new SpotlightItem("one", "One", () => { selected.Add("one"); return Task.CompletedTask; }),
            new SpotlightItem("two", "Two", () => { selected.Add("two"); return Task.CompletedTask; }),
        };
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("main", "Commands", items)])
            .Add(x => x.OpenChanged, value => states.Add(value)));
        var input = cut.Find("[role=combobox]");
        Assert.Equal("One", cut.Find("[role=option]").TextContent);
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal([false], states);
        Assert.Equal(["two"], selected);
    }

    [Fact]
    public void QueryLoadingEmptyAndInvalidIdentitiesRemainCallerOwned()
    {
        string? query = null;
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Query, "a").Add(x => x.QueryChanged, value => query = value)
            .Add(x => x.Sections, [new SpotlightSection("remote", "Records", [], true, "Loading records")])
            .Add(x => x.Empty, "Nothing found"));
        cut.Find("input").Input("abc");
        Assert.Equal("abc", query);
        Assert.Equal("Loading records", cut.Find(".hl-spotlight__loading").TextContent);
        Assert.ThrowsAny<Exception>(() => Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("x", "X", [new SpotlightItem("same", "A", () => Task.CompletedTask), new SpotlightItem("same", "B", () => Task.CompletedTask)])])));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.spotlight")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); var id = fixture.RootElement.GetProperty("id").GetString(); Assert.StartsWith("spotlight.", id);
        if (id != "spotlight.classes") { Assert.Empty(Render<HarborlineSpotlight>(p => p.Add(x => x.AriaLabel, "Search")).FindAll("[role=dialog]")); return; }

        // 282 s11: this lane once spelled the query input hl-spotlight__input as well and put
        // hl-visually-hidden on the status node; neither is defined by the authority stylesheet, and
        // hl-spotlight__group was styled by neither lane. The React case asserts the same fixture row.
        var expected = fixture.RootElement.GetProperty("expected");
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("commands", "Commands", [new SpotlightItem("one", "One", () => Task.CompletedTask)])]));
        Assert.Equal(Classes(expected, "queryClasses"), cut.Find("[role=combobox]").ClassList);
        Assert.Equal(Classes(expected, "statusClasses"), cut.Find("[role=status]").ClassList);
        Assert.Equal(Classes(expected, "groupClasses"), cut.Find("[role=group]").ClassList);
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
