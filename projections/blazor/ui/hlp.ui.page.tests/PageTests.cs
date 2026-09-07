using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class PageTests : BunitContext
{
    [Fact] public void DefaultsOwnHeaderButNeverMain(){var cut=Render<HarborlinePage>(p=>p.Add(x=>x.Title,"Health").AddChildContent("Body"));Assert.Single(cut.FindAll("header"));Assert.Empty(cut.FindAll("main"));Assert.Equal("Health",cut.Find("h1").TextContent);Assert.Equal("0",cut.Find("section").GetAttribute("tabindex"));Assert.Equal("true",cut.Find("header").GetAttribute("data-sticky"));}
    [Fact] public void HeadingAndPaddingAreClosed(){var cut=Render<HarborlinePage>(p=>p.Add(x=>x.Title,"Health").Add(x=>x.HeadingLevel,PageHeadingLevel.H3).Add(x=>x.BodyPadding,PageBodyPadding.Lg));Assert.Equal("Health",cut.Find("h3").TextContent);Assert.Contains("hl-page__body--padding-lg",cut.Find("section").ClassList);}
    [Fact] public void RootAndBodyClassesStaySeparated(){var cut=Render<HarborlinePage>(p=>p.Add(x=>x.Title,"Health").Add(x=>x.Class,"root-owner").Add(x=>x.BodyClass,"body-owner"));Assert.Contains("root-owner",cut.Find(".hl-page").ClassList);Assert.DoesNotContain("body-owner",cut.Find(".hl-page").ClassList);Assert.Contains("body-owner",cut.Find("section").ClassList);}
    [Fact,Trait("ModuleConformance","hlp.ui.page")] public void SharedFixtureConforms(){AssertFixturePrefix("page.");Assert.NotNull(Render<HarborlinePage>(p=>p.Add(x=>x.Title,"Page")));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
