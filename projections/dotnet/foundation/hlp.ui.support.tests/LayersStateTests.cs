using Harborline.Foundation.Builder;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class LayersStateTests
{
    [Fact]
    public void InitializationAndTransitionsAreImmutableAndAppEquivalent()
    {
        var omitted = LayersState.Initialize(["layout", "rules"]);
        Assert.Equal("layout", omitted.ActiveId);
        Assert.Equal(["layout"], omitted.EnabledIds);

        var explicitNull = LayersState.Initialize(["layout", "rules"], null);
        var active = explicitNull.Activate("layout").Activate("rules");
        Assert.Equal("rules", active.ActiveId);
        Assert.Equal(["layout", "rules"], active.EnabledIds.Order());
        Assert.Equal("layout", omitted.ActiveId);
        Assert.Null(active.Clear().ActiveId);
        Assert.Equal(["layout", "rules"], active.Clear().EnabledIds.Order());
        Assert.Null(active.Toggle("rules").ActiveId);
        Assert.Equal(["layout"], active.Toggle("rules").EnabledIds);
        Assert.Same(active, active.Activate("rules"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.layers-state")]
    public void SharedFixtureConforms()
    {
        using var fixture = Fixture.Read("layers.");
        if (fixture is null) return;
        Assert.Contains(fixture.RootElement.GetProperty("id").GetString(), Fixture.CaseIds("hlp.ui.layers-state"));
        Assert.Null(LayersState.Initialize([], null).ActiveId);
    }
}
