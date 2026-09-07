using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class BadgeTests : BunitContext
{
    [Fact] public void AxesOverlayAndLiveRegionMatch(){var cut=Render<HarborlineBadge>(p=>p.Add(x=>x.ThemeColor,BadgeThemeColor.Success).Add(x=>x.FillMode,BadgeFillMode.Outline).Add(x=>x.Rounded,BadgeRounded.Full).Add(x=>x.Align,new BadgeAlign(BadgeHorizontal.End,BadgeVertical.Top)).Add(x=>x.Position,BadgePosition.Edge).Add(x=>x.AnnounceChanges,true).AddChildContent("3 updates"));Assert.Contains("hl-badge--success",cut.Find(".hl-badge").ClassList);Assert.Equal("polite",cut.Find("[aria-live]").GetAttribute("aria-live"));}
    [Fact] public void ExplicitAxesOverrideDeprecatedAliases(){var cut=Render<HarborlineBadge>(p=>p.Add(x=>x.Appearance,BadgeAppearance.Solid).Add(x=>x.FillMode,BadgeFillMode.Outline).Add(x=>x.Shape,BadgeShape.Pill).Add(x=>x.Rounded,BadgeRounded.None).AddChildContent("Status"));Assert.Equal("outline",cut.Find(".hl-badge").GetAttribute("data-hl-fill"));Assert.Contains("hl-badge--rounded-none",cut.Find(".hl-badge").ClassList);}
    [Fact,Trait("ModuleConformance","hlp.ui.badge")] public void SharedFixtureConforms(){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith("badge.",fixture.RootElement.GetProperty("id").GetString());Assert.Contains("Draft",Render<HarborlineBadge>(p=>p.AddChildContent("Draft")).Markup);}
}
