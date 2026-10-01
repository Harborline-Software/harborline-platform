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
    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
