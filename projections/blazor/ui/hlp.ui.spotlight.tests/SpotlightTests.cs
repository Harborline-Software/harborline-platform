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

    // b19: a listbox owns only options and groups (ARIA required owned elements; axe
    // aria-required-children). The empty copy sits beside the empty listbox, not inside it.
    [Fact]
    public void EmptyCopySitsOutsideTheListbox()
    {
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, []).Add(x => x.Empty, "Nothing found"));
        Assert.Equal("Nothing found", cut.Find(".hl-spotlight__empty").TextContent);
        Assert.Empty(cut.Find("[role=listbox]").Children);
        Assert.Empty(cut.FindAll("[role=listbox] .hl-spotlight__empty"));
    }

    // b20: WCAG 4.1.3 — a screen-reader user who types to zero results hears the caller's empty
    // copy from the status region that existed before the change, even without ResultsCountLabel.
    [Fact]
    public void ZeroResultsAreAnnouncedThroughThePreExistingPoliteStatus()
    {
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("main", "Commands", [new SpotlightItem("one", "One", () => Task.CompletedTask)])])
            .Add(x => x.Empty, "Aucun résultat"));
        var status = cut.Find("[role=status]");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal("", status.TextContent);
        // bUnit rebuilds its DOM per render, so node identity is not observable here; the region is
        // unconditional markup, so Blazor's diff only replaces its text (the browser probe checks identity).
        cut.Render(p => p.Add(x => x.Query, "zz").Add(x => x.Sections, []));
        Assert.Single(cut.FindAll("[role=status][aria-live=polite]"));
        Assert.Equal("Aucun résultat", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void TheCountIsNotAnnouncedBesideTheEmptyCopy()
    {
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, []).Add(x => x.Empty, "Aucun résultat").Add(x => x.ResultsCountLabel, count => $"{count} résultats"));
        Assert.Equal("Aucun résultat", cut.Find("[role=status]").TextContent);
    }

    // APG combobox: aria-expanded is true only while the popup is displayed; an empty listbox shows nothing.
    [Fact]
    public void ThePopupIsCollapsedWhileThereAreNoResultsAndNoLoadingSection()
    {
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("main", "Commands", [new SpotlightItem("one", "One", () => Task.CompletedTask)])]));
        Assert.Equal("true", cut.Find("[role=combobox]").GetAttribute("aria-expanded"));
        cut.Render(p => p.Add(x => x.Sections, []));
        Assert.Equal("false", cut.Find("[role=combobox]").GetAttribute("aria-expanded"));
        Assert.Equal(cut.Find("[role=listbox]").Id, cut.Find("[role=combobox]").GetAttribute("aria-controls"));
        cut.Render(p => p.Add(x => x.Sections, [new SpotlightSection("r", "Records", [], true, "Loading")]));
        Assert.Equal("true", cut.Find("[role=combobox]").GetAttribute("aria-expanded"));
    }

    // b19 review: a loading section is a labelled group holding its loading text, as in React; the
    // listbox owns only groups and options.
    [Fact]
    public void LoadingSectionIsALabelledGroupInsideTheListbox()
    {
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Sections, [new SpotlightSection("remote", "Records", [], true, "Loading records")]));
        var listbox = cut.Find("[role=listbox]");
        Assert.All(listbox.Children, child => Assert.Equal("group", child.GetAttribute("role")));
        var group = cut.Find("[role=listbox] > [role=group]");
        Assert.Equal("Records", group.GetAttribute("aria-label"));
        Assert.Equal("Loading records", group.QuerySelector(".hl-spotlight__loading")!.TextContent);
        Assert.Empty(cut.FindAll("[role=option]"));
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.spotlight")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.spotlight", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw); var id = fixture.RootElement.GetProperty("id").GetString(); Assert.StartsWith("spotlight.", id);
            if (id == "spotlight.empty-announced") { AssertEmptyAnnounced(fixture.RootElement); return; }
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
        });
    }

    // b20 (WCAG 4.1.3 Status Messages; APG combobox): typing to zero results is announced through
    // the polite status region that was already in the DOM, in place of (never beside) the count,
    // and the combobox stops claiming an expanded popup while the listbox shows nothing. The React
    // case asserts the same fixture row.
    private void AssertEmptyAnnounced(System.Text.Json.JsonElement fixture)
    {
        var input = fixture.GetProperty("input");
        var expected = fixture.GetProperty("expected");
        var pattern = input.GetProperty("resultsCountLabel").GetString()!;
        Func<int, string> countLabel = count => pattern.Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var before = input.GetProperty("before").EnumerateArray()
            .Select(item => new SpotlightItem(item.GetString()!, item.GetString()!, () => Task.CompletedTask)).ToArray();
        var cut = Render<HarborlineSpotlight>(p => p.Add(x => x.Open, true).Add(x => x.AriaLabel, "Search")
            .Add(x => x.Empty, input.GetProperty("empty").GetString()).Add(x => x.ResultsCountLabel, countLabel)
            .Add(x => x.Sections, [new SpotlightSection("commands", "Commands", before)]));
        var status = cut.Find("[role=status]");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal(expected.GetProperty("statusBefore").GetString(), status.TextContent);
        Assert.Equal(expected.GetProperty("expandedBefore").GetString(), cut.Find("[role=combobox]").GetAttribute("aria-expanded"));

        cut.Render(p => p.Add(x => x.Query, "zz").Add(x => x.Sections, []));
        Assert.Single(cut.FindAll("[role=status][aria-live=polite]"));
        Assert.Equal(expected.GetProperty("statusAfter").GetString(), cut.Find("[role=status]").TextContent);
        var combobox = cut.Find("[role=combobox]");
        Assert.Equal(expected.GetProperty("expandedAfter").GetString(), combobox.GetAttribute("aria-expanded"));
        Assert.False(combobox.HasAttribute("aria-activedescendant"));

        var loading = input.GetProperty("loading");
        cut.Render(p => p.Add(x => x.Sections, [new SpotlightSection(loading.GetProperty("id").GetString()!,
            loading.GetProperty("label").GetString()!, [], true, loading.GetProperty("loadingLabel").GetString())]));
        Assert.Equal(expected.GetProperty("statusWhileLoading").GetString(), cut.Find("[role=status]").TextContent);
        Assert.Equal(expected.GetProperty("expandedWhileLoading").GetString(), cut.Find("[role=combobox]").GetAttribute("aria-expanded"));
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
