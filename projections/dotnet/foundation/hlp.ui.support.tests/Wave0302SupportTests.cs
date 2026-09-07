using Harborline.Foundation.Responsive;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class Wave0302SupportTests
{
    [Fact]
    public void UncontrolledResponsiveStatePreservesBasePreference()
    {
        var controller = new NavCollapseController(initialNarrow: true);
        Assert.True(controller.Snapshot.Collapsed);
        Assert.False(controller.Snapshot.BaseCollapsed);
        controller.UpdateViewport(false, false);
        Assert.False(controller.Snapshot.Collapsed);
        controller.SetCollapsed(false);
        controller.UpdateViewport(true, true);
        Assert.False(controller.Snapshot.Collapsed);
        Assert.True(controller.Snapshot.IsOverlay);
    }

    [Fact]
    public void ControlledCrossingRequestsCollapseOnce()
    {
        var requests = new List<bool>();
        var controller = new NavCollapseController(new(Collapsed: false), onCollapsedChange: requests.Add);
        controller.UpdateViewport(false, false);
        controller.UpdateViewport(true, false);
        controller.UpdateViewport(true, false);
        Assert.Equal([true], requests);
        Assert.True(controller.Snapshot.Collapsed);
    }

    [Fact]
    public void InitialNarrowControlledStateDoesNotEmit()
    {
        var requests = new List<bool>();
        var controller = new NavCollapseController(new(Collapsed: false), initialNarrow: true, onCollapsedChange: requests.Add);
        Assert.True(controller.Snapshot.Collapsed);
        Assert.Empty(requests);
    }

    [Fact]
    public void ThresholdsAreAuthoredAndZeroDisablesMatches()
    {
        Assert.Equal("(max-width: 899px)", NavCollapseController.Query(900));
        Assert.Equal("(max-width: 0px)", NavCollapseController.Query(0));
        var controller = new NavCollapseController(new(AutoCollapseBelow: 0, OverlayBelow: 0), true, true);
        Assert.Equal(new(false, false, false, false), controller.Snapshot);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.use-nav-collapsed")]
    public void SharedFixturesRemainBoundToFoundation()
    {
        using var fixture = Fixture.Read("nav-collapse.");
        if (fixture is null) return;
        Assert.Contains(fixture.RootElement.GetProperty("id").GetString(), Fixture.CaseIds("hlp.ui.use-nav-collapsed"));
        Assert.Equal("Harborline.Foundation", typeof(NavCollapseController).Assembly.GetName().Name);
    }
}
