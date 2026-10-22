using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TextBoxTests : BunitContext
{
    [Fact] public void ControlledChangeRequestsWithoutLocalMutation() { var requests = new List<string>(); var cut = Render<HarborlineTextBox>(p => p.Add(x => x.Value, "alpha").Add(x => x.ValueChanged, value => requests.Add(value))); cut.Find("input").Input("beta"); Assert.Equal(["beta"], requests); Assert.Equal("alpha", cut.Find("input").GetAttribute("value")); }
    [Fact] public void UncontrolledClearAndRevealAreKeyboardReachable() { JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose; var requests = new List<string>(); var clear = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "alpha").Add(x => x.ClearButton, true).Add(x => x.ValueChanged, value => requests.Add(value))); clear.Find("button").Click(); Assert.Equal([string.Empty], requests); Assert.Equal(string.Empty, clear.Find("input").GetAttribute("value")); var password = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "secret").Add(x => x.Type, "password").Add(x => x.ShowReveal, true)); Assert.Equal("password", password.Find("input").GetAttribute("type")); password.Find("button").Click(); Assert.Equal("text", password.Find("input").GetAttribute("type")); Assert.NotEqual("-1", password.Find("button").GetAttribute("tabindex")); }
    [Fact] public void DisabledPasswordCannotReveal() { var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "secret").Add(x => x.Type, "password").Add(x => x.ShowReveal, true).Add(x => x.Disabled, true)); Assert.Empty(cut.FindAll("button")); Assert.Equal("password", cut.Find("input").GetAttribute("type")); }
    [Fact] public void ComposesInputAndPreservesCallerSuffix() { var cut = Render<HarborlineTextBox>(p => p.Add(x => x.Value, "alpha").Add(x => x.ClearButton, true).Add(x => x.Suffix, (RenderFragment)(builder => builder.AddContent(0, "USD"))).Add(x => x.Prefix, (RenderFragment)(builder => builder.AddContent(0, "$")))); Assert.NotNull(cut.Find("span.hl-input")); Assert.Equal("$", cut.Find("span.hl-input__prefix").TextContent); Assert.Equal("USD", cut.Find("span.hl-text-box__caller-suffix").TextContent); Assert.Equal("clear", cut.Find("button.hl-text-box__action").GetAttribute("data-hl-action")); }
    // Contract: clear emits the empty string once and retains focus in the text-entry group. The clear
    // control disappears with the value, so a keyboard user's focus must move back to the input.
    [Fact] public void ClearReturnsFocusToTheInput()
    {
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js"); module.Mode = JSRuntimeMode.Loose;
        var requests = new List<string>();
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.DefaultValue, "alpha").Add(x => x.ClearButton, true).Add(x => x.ValueChanged, value => requests.Add(value)));
        var inputId = cut.Find("input").Id;
        cut.Find("[data-hl-action=clear]").Click();
        Assert.Equal([""], requests);
        Assert.Empty(cut.FindAll("[data-hl-action=clear]"));
        var focus = Assert.Single(module.Invocations["focusInput"]);
        Assert.Equal(inputId, focus.Arguments[0]);
    }
    [Theory, Trait("ModuleConformance", "hlp.ui.text-box")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.text-box", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixture("text-box.");
        });
    }
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
