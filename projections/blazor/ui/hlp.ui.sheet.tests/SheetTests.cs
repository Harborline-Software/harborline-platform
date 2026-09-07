using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SheetTests : BunitContext
{
    public SheetTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void TriggerOpensUncontrolledSheetAndCloseControlsDismissExactlyOnce()
    {
        var states = new List<bool>();
        var cut = Render<HarborlineSheet>(p => p.Add(x => x.OpenChanged, value => states.Add(value)).Add(x => x.ChildContent, Content()));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.Find(".hl-sheet__trigger").Click();
        Assert.Equal("true", cut.Find("[role=dialog]").GetAttribute("aria-modal"));
        Assert.Equal("right", cut.Find("[role=dialog]").GetAttribute("data-side"));
        Assert.Equal("Open calendar", cut.Find("[role=dialog]").GetAttribute("aria-labelledby") is not null ? cut.Find("h2").TextContent : null);
        cut.Find(".hl-sheet__built-in-close").Click();
        Assert.Equal([true, false], states);
    }

    [Fact]
    public void ControlledAndNonmodalStateRemainHostOwned()
    {
        var states = new List<bool>();
        var closed = Render<HarborlineSheet>(p => p.Add(x => x.Open, false).Add(x => x.Modal, false)
            .Add(x => x.OpenChanged, value => states.Add(value)).Add(x => x.ChildContent, Content()));
        closed.Find("button").Click();
        Assert.Empty(closed.FindAll("[role=dialog]"));
        Assert.Equal([true], states);
        var open = Render<HarborlineSheet>(p => p.Add(x => x.Open, true).Add(x => x.Modal, false).Add(x => x.ChildContent, Content()));
        Assert.Equal("false", open.Find("[role=dialog]").GetAttribute("aria-modal"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.sheet")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        var id = fixture.RootElement.GetProperty("id").GetString(); Assert.StartsWith("sheet.", id);
        if (id != "sheet.close-classes")
        {
            Assert.Empty(Render<HarborlineSheet>(p => p.Add(x => x.ChildContent, Content())).FindAll("[role=dialog]"));
            return;
        }

        // 282 s9: the Blazor lane also spelled the built-in close hl-sheet__builtin-close, which the
        // authority stylesheet never defined. This asserts the one spelling in this lane; the React
        // case asserts the same fixture row in the other.
        var expected = fixture.RootElement.GetProperty("expected").GetProperty("builtInCloseClasses")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var open = Render<HarborlineSheet>(p => p.Add(x => x.Open, true).Add(x => x.ChildContent, Content()));
        var close = open.Find("[role=dialog] button[aria-label=Close]");
        Assert.Equal(expected, close.ClassList);
    }

    private static RenderFragment Content() => builder =>
    {
        builder.OpenComponent<HarborlineSheetTrigger>(0); builder.AddAttribute(1, nameof(HarborlineSheetTrigger.ChildContent), (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
        builder.OpenComponent<HarborlineSheetContent>(2); builder.AddAttribute(3, nameof(HarborlineSheetContent.CloseLabel), "Close"); builder.AddAttribute(4, nameof(HarborlineSheetContent.HasDescription), true);
        builder.AddAttribute(5, nameof(HarborlineSheetContent.ChildContent), (RenderFragment)(content =>
        {
            content.OpenComponent<HarborlineSheetHeader>(0); content.AddAttribute(1, nameof(HarborlineSheetHeader.ChildContent), (RenderFragment)(header =>
            {
                header.OpenComponent<HarborlineSheetTitle>(0); header.AddAttribute(1, nameof(HarborlineSheetTitle.ChildContent), (RenderFragment)(t => t.AddContent(0, "Open calendar"))); header.CloseComponent();
                header.OpenComponent<HarborlineSheetDescription>(2); header.AddAttribute(3, nameof(HarborlineSheetDescription.ChildContent), (RenderFragment)(d => d.AddContent(0, "Choose a date"))); header.CloseComponent();
            })); content.CloseComponent();
            content.OpenComponent<HarborlineSheetClose>(2); content.AddAttribute(3, nameof(HarborlineSheetClose.ChildContent), (RenderFragment)(c => c.AddContent(0, "Done"))); content.CloseComponent();
        })); builder.CloseComponent();
    };
}
