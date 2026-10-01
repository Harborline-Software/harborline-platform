using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SwitchFieldTests : BunitContext
{
    [Fact] public void NameSuppliesIdAndControlledActivationRequestsOnce() { bool? requested = null; var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "published").Add(x => x.Checked, false).Add(x => x.Label, "Published").Add(x => x.CheckedChanged, value => requested = value)); var button = cut.Find("[role=switch]"); Assert.Equal("published", button.Id); button.Click(); Assert.True(requested); Assert.Equal("false", button.GetAttribute("aria-checked")); }
    [Fact] public void DisabledSuppressesRequests() { var count = 0; var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "enabled").Add(x => x.Label, "Enabled").Add(x => x.Disabled, true).Add(x => x.CheckedChanged, _ => count++)); cut.Find("button").Click(); Assert.Equal(0, count); }
    [Theory, Trait("ModuleConformance", "hlp.ui.switch-field")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.switch-field", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("switch-field.");
        });
    }
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
