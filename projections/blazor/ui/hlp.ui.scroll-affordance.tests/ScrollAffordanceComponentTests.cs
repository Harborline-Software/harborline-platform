using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ScrollAffordanceComponentTests : BunitContext
{
    public ScrollAffordanceComponentTests() => JSInterop.Mode = JSRuntimeMode.Loose;
    [Fact] public void NamedRegionPreservesContentAndOrientation() { var cut = Render<HarborlineScrollAffordance>(p => p.Add(x => x.AriaLabel, "Results").Add(x => x.Orientation, Browser.ScrollAffordanceOrientation.Vertical).Add(x => x.ChildContent, Text("Body"))); var host = cut.Find("[role=group]"); Assert.Equal("Results", host.GetAttribute("aria-label")); Assert.Contains("vertical", host.ClassName); Assert.Equal("Body", host.TextContent); }
    [Theory, Trait("ModuleConformance", "hlp.ui.scroll-affordance")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.scroll-affordance", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("scroll-affordance.component-"); Assert.NotNull(Render<HarborlineScrollAffordance>(p => p.Add(x => x.AriaLabel, "Items")));
        });
    }
    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
