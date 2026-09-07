using Bunit;
using Harborline.Foundation.Enums;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class IconButtonTests : BunitContext
{
    [Fact]
    public void NameTypeLoadingAndActivationFollowNativeButtonSemantics()
    {
        var calls = 0;
        var cut = Render<HarborlineIconButton>(p => p.Add(x => x.AccessibleName, "Open layers").Add(x => x.ButtonType, ButtonType.Submit).Add(x => x.OnClick, () => calls++).Add(x => x.Icon, b => b.AddContent(0, "+")));
        Assert.Equal("Open layers", cut.Find("button").GetAttribute("aria-label"));
        Assert.Equal("submit", cut.Find("button").GetAttribute("type"));
        cut.Find("button").Click(); Assert.Equal(1, calls);
        cut.Render(p => p.Add(x => x.AccessibleName, "Open layers").Add(x => x.Loading, true).Add(x => x.OnClick, () => calls++));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        Assert.Equal("true", cut.Find("button").GetAttribute("aria-busy"));
        Assert.Empty(cut.FindAll("[role=status]"));
    }

    [Fact]
    public void EmptyAccessibleNameFailsClosed() => Assert.Contains("accessible-name-required", Assert.ThrowsAny<Exception>(() => Render<HarborlineIconButton>()).ToString());

    [Fact, Trait("ModuleConformance", "hlp.ui.icon-button")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("icon-button.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("button", Render<HarborlineIconButton>(p => p.Add(x => x.AccessibleName, "Action")).Find("button").GetAttribute("type"));
    }
}
