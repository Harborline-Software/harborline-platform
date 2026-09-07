using Harborline.UIAdapters.Blazor.Accessibility;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TouchTargetNativeTests
{
    [Fact]
    public void StrategiesKeepTheFortyFourPixelPolicyAndPositioningDistinct()
    {
        Assert.Equal(44, TouchTargetAffordances.MinimumCssPixels);
        Assert.Equal(3, Enum.GetValues<TouchTargetStrategy>().Length);
        Assert.DoesNotContain("positioned", TouchTargetAffordances.For(TouchTargetStrategy.OverlayUsingExistingPositionContext));
        Assert.Contains("positioned", TouchTargetAffordances.For(TouchTargetStrategy.OverlayEstablishingPositionContext));
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.touch-target")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("touch-target.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal(44, TouchTargetAffordances.MinimumCssPixels);
    }
}
