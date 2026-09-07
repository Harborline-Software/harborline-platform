using Harborline.Foundation.Builder;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class RailLabelsConformanceTests
{
    [Fact]
    public void ExactDefaultsAndAllSixteenMembersRemainAvailable()
    {
        var labels = DefaultRailLabels.Value;
        Assert.Equal("Lenses", labels.LensesHeading);
        Assert.Equal("Toggle Validation lens", labels.ToggleLens("Validation"));
        Assert.Equal("Show Validation lens on the canvas", labels.ActivateLens("Validation"));
        Assert.Equal("+12", labels.PassiveCount(12));
        Assert.Equal("Viewing: Access", labels.ViewingLens("Access"));
        Assert.Equal("press 3", labels.LensShortcutHint(3));
        Assert.Equal(16, typeof(RailLabels).GetProperties().Length);
    }

    [Fact]
    [Trait("ModuleConformance", "hlp.ui.rail-labels")]
    public void SharedFixtureConforms()
    {
        using var fixture = Fixture.Read("rail-labels.");
        if (fixture is null) return;
        Assert.Contains(fixture.RootElement.GetProperty("id").GetString(), Fixture.CaseIds("hlp.ui.rail-labels"));
        Assert.Equal(16, typeof(RailLabels).GetProperties().Length);
        Assert.Equal("سلامة", DefaultRailLabels.Value.Source("سلامة"));
    }
}
