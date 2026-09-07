using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class TextAreaTests : BunitContext
{
    [Fact]
    public void DefaultsAndRawInputRemainNative()
    {
        string? emitted = null;
        var cut = Render<HarborlineTextArea>(p => p.Add(x => x.Name, "notes").Add(x => x.ValueChanged, value => emitted = value));
        var textarea = cut.Find("textarea");
        Assert.Equal("3", textarea.GetAttribute("rows"));
        Assert.Contains("hl-text-area--resize-vertical", textarea.ClassList);
        textarea.Input("  raw\ntext  ");
        Assert.Equal("  raw\ntext  ", emitted);
    }

    [Fact]
    public void ControlledCounterTracksRenderedValue()
    {
        var cut = Render<HarborlineTextArea>(p => p.Add(x => x.Value, "one").Add(x => x.ShowCounter, true).Add(x => x.MaxLength, 10));
        Assert.Equal("3/10", cut.Find(".hl-text-area__counter").TextContent);
        cut.Render(p => p.Add(x => x.Value, "updated").Add(x => x.ShowCounter, true).Add(x => x.MaxLength, 10));
        Assert.Equal("7/10", cut.Find(".hl-text-area__counter").TextContent);
    }

    [Fact]
    public void ExplicitFalseOverridesAmbientRequiredAndDisabled()
    {
        RenderFragment child = builder =>
        {
            builder.OpenComponent<HarborlineTextArea>(0);
            builder.AddAttribute(1, nameof(HarborlineTextArea.Required), false);
            builder.AddAttribute(2, nameof(HarborlineTextArea.Disabled), false);
            builder.CloseComponent();
        };
        var cut = Render<HarborlineFormField>(p => p.Add(x => x.Name, "notes").Add(x => x.Label, "Notes")
            .Add(x => x.Required, true).Add(x => x.Disabled, true).Add(x => x.Hint, "Explain").Add(x => x.ChildContent, child));
        var textarea = cut.Find("textarea");
        Assert.False(textarea.HasAttribute("disabled"));
        Assert.False(textarea.HasAttribute("required"));
        Assert.Equal("notes-label", textarea.GetAttribute("aria-labelledby"));
        Assert.Equal("notes-hint", textarea.GetAttribute("aria-describedby"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.text-area")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("text-area.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("3", Render<HarborlineTextArea>().Find("textarea").GetAttribute("rows"));
    }
}
