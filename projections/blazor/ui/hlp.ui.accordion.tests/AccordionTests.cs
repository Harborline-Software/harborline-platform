using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AccordionTests : BunitContext
{
    private static readonly IReadOnlyList<HarborlineAccordionItem> Items =
    [
        new("a", b => b.AddContent(0, "Alpha"), b => b.AddContent(0, "A panel")),
        new("b", b => b.AddContent(0, "Beta"), b => b.AddContent(0, "B panel"), true),
        new("c", b => b.AddContent(0, "Gamma"), b => b.AddContent(0, "C panel")),
    ];

    [Fact]
    public void SingleUncontrolledDisabledAndRelationshipsMatchContract()
    {
        IReadOnlyList<string>? changed = null;
        var cut = Render<HarborlineAccordion>(p => p.Add(x => x.Items, Items).Add(x => x.DefaultExpandedValues, ["a", "b", "missing"]).Add(x => x.ExpandedValuesChanged, values => changed = values).Add(x => x.Class, "consumer"));
        var buttons = cut.FindAll("button");
        Assert.Equal("true", buttons[0].GetAttribute("aria-expanded"));
        Assert.Equal("false", buttons[2].GetAttribute("aria-expanded"));
        Assert.True(buttons[1].HasAttribute("disabled"));
        AssertHeadersNameAndControlTheirPanels(cut);
        buttons[2].Click();
        Assert.Equal(["c"], changed);
        AssertHeadersNameAndControlTheirPanels(cut);
        Assert.Contains("consumer", cut.Find(".hl-accordion").ClassList);
    }

    [Fact]
    public void AHeaderReRenderedToTakeFocusKeepsItsNameStateAndPanel()
    {
        // Arrow keys re-render the target header through the branch that captures it for focus. That branch
        // repeats the header markup, so its name, aria-expanded and panel wiring are asserted there too.
        var cut = Render<HarborlineAccordion>(p => p.Add(x => x.Items, Items).Add(x => x.DefaultExpandedValues, ["a"]));
        cut.FindAll("button")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" }); // past disabled Beta to Gamma
        JSInterop.VerifyFocusAsyncInvoke(1);
        Assert.Equal("false", cut.FindAll("button")[2].GetAttribute("aria-expanded"));
        AssertHeadersNameAndControlTheirPanels(cut);

        cut.FindAll("button")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" }); // wraps to expanded Alpha
        JSInterop.VerifyFocusAsyncInvoke(2);
        Assert.Equal("true", cut.FindAll("button")[0].GetAttribute("aria-expanded"));
        AssertHeadersNameAndControlTheirPanels(cut);
    }

    // APG accordion: each header button is named by its heading text and controls its own panel, and each
    // panel is a region named by that header through aria-labelledby. Ids resolve, once each.
    private static void AssertHeadersNameAndControlTheirPanels(IRenderedComponent<HarborlineAccordion> cut)
    {
        Accname.AssertIdReferencesResolve(cut);
        var headers = cut.FindAll("button");
        var panels = cut.FindAll("[role=region]");
        string[] headings = ["Alpha", "Beta", "Gamma"];
        Assert.Equal(headings.Length, headers.Count);
        Assert.Equal(headings.Length, panels.Count);
        for (var index = 0; index < headings.Length; index++)
        {
            Assert.Equal(headings[index], Accname.AccessibleName(headers[index]));
            Accname.AssertReferences(headers[index], "aria-controls", panels[index]);
            Accname.AssertReferences(panels[index], "aria-labelledby", headers[index]);
            Assert.Equal(headings[index], Accname.AccessibleName(panels[index]));
        }
    }

    [Fact]
    public void ControlledAndNoncollapsibleModesDoNotMutateLocally()
    {
        var calls = 0;
        var cut = Render<HarborlineAccordion>(p => p.Add(x => x.Items, Items).Add(x => x.ExpandedValues, ["a"]).Add(x => x.ExpandedValuesChanged, _ => calls++));
        cut.FindAll("button")[2].Click();
        Assert.Equal(1, calls);
        Assert.Equal("true", cut.FindAll("button")[0].GetAttribute("aria-expanded"));
        var fixedCut = Render<HarborlineAccordion>(p => p.Add(x => x.Items, Items).Add(x => x.Collapsible, false));
        fixedCut.FindAll("button")[0].Click();
        Assert.Equal("true", fixedCut.FindAll("button")[0].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void EmptyCollectionRendersCallerTextOrTheDefaultSentence()
    {
        var fixture = FindFixture("accordion.empty");
        var text = fixture.GetProperty("input").GetProperty("empty").GetString()!;
        var expected = fixture.GetProperty("expected");
        var cut = Render<HarborlineAccordion>(p => p
            .Add(x => x.Items, Array.Empty<HarborlineAccordionItem>())
            .Add(x => x.Empty, text));
        Assert.Equal(expected.GetProperty("emptyText").GetString(), cut.Find(".hl-accordion__empty").TextContent);
        Assert.Equal(expected.GetProperty("headers").GetInt32(), cut.FindAll("button").Count);

        var bare = Render<HarborlineAccordion>(p => p.Add(x => x.Items, Array.Empty<HarborlineAccordionItem>()));
        Assert.Equal(expected.GetProperty("defaultEmptyText").GetString(), bare.Find("[data-hl-empty]").TextContent);
    }

    [Fact]
    public void BlankEmptyTextFallsBackToTheDefaultSentence()
    {
        var fixture = FindFixture("accordion.blankEmptyText");
        var text = fixture.GetProperty("input").GetProperty("empty").GetString()!;
        var expected = fixture.GetProperty("expected");
        var cut = Render<HarborlineAccordion>(p => p
            .Add(x => x.Items, Array.Empty<HarborlineAccordionItem>())
            .Add(x => x.Empty, text));
        Assert.Equal(expected.GetProperty("defaultEmptyText").GetString(), cut.Find(".hl-accordion__empty").TextContent);
        Assert.Equal(expected.GetProperty("headers").GetInt32(), cut.FindAll("button").Count);
    }

    // The fixture is the authority: the empty copy these tests assert comes from
    // conformance/hlp.ui.accordion/fixtures.yaml, not from a snapshot pasted here.
    private static System.Text.Json.JsonElement FindFixture(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "catalog", "modules.yaml")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "conformance", "hlp.ui.accordion", "fixtures.yaml");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
            if (element.GetProperty("id").GetString() == id) return element.Clone();
        throw new Xunit.Sdk.XunitException($"missing-neutral-fixture: {id}");
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.accordion")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.accordion", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current;
            if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = System.Text.Json.JsonDocument.Parse(raw);
            Assert.StartsWith("accordion.", fixture.RootElement.GetProperty("id").GetString());
            Assert.Equal(3, Render<HarborlineAccordion>(p => p.Add(x => x.Items, Items)).FindAll("button").Count);
        });
    }
}
