using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DialogTests : BunitContext
{
    public DialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;
    [Fact] public void ClosedUnmountsAndOpenRelatesTitleDescription() { var closed = Render<HarborlineDialog>(p => p.Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body"))); Assert.Empty(closed.FindAll("[role=dialog]")); var open = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.Description, "Details").Add(x => x.ChildContent, Text("Body"))); var dialog = open.Find("[role=dialog]"); Assert.Equal("true", dialog.GetAttribute("aria-modal")); Assert.Equal("-1", dialog.GetAttribute("tabindex")); Assert.NotNull(dialog.GetAttribute("aria-labelledby")); Assert.NotNull(dialog.GetAttribute("aria-describedby")); }
    [Fact] public void CloseButtonRequestsFalseExactlyOnce() { var requests = new List<bool>(); var cut = Render<HarborlineDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Edit").Add(x => x.ChildContent, Text("Body")).Add(x => x.OpenChanged, value => requests.Add(value))); cut.Find(".hl-dialog__close").Click(); Assert.Equal([false], requests); }
    [Theory, Trait("ModuleConformance", "hlp.ui.dialog")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.dialog", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("dialog.");
        });
    }
    // T-1122: the translate is physical, so the start edge must be too, or right-to-left content
    // moves the surface off the left edge. This deliberately pins the chosen implementation and the
    // byte identity of the copies; the behaviour itself is proved by gallery/tests/tests/dialog-rtl.spec.ts.
    [Fact]
    public void LaneStylesheetCentresTheSurfaceWithAPhysicalLeftEdge()
    {
        var lane = File.ReadAllText(Repo("projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.css"));
        Assert.Equal(File.ReadAllText(Repo("specs/modules/ui/hlp.ui.dialog/style.css")), lane);
        var surface = System.Text.RegularExpressions.Regex.Match(lane, @"\n\.hl-dialog__surface \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("\n  left: 50%;", surface, StringComparison.Ordinal);
        Assert.Contains("transform: translate(-50%, -50%)", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  inset-inline-start:", surface, StringComparison.Ordinal);
    }
    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);
    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
