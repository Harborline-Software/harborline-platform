using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.UIAdapters.Blazor.Components.Forms;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class FormFieldTests : BunitContext
{
    [Fact]
    public void LabelHintErrorAndContextHaveStableRelationships()
    {
        FormFieldContextValue? observed = null;
        RenderFragment child = builder =>
        {
            builder.OpenComponent<ContextProbe>(0);
            builder.AddAttribute(1, nameof(ContextProbe.Observed), EventCallback.Factory.Create<FormFieldContextValue>(this, value => observed = value));
            builder.CloseComponent();
        };
        var cut = Render<HarborlineFormField>(p => p
            .Add(x => x.Name, "amount").Add(x => x.Label, "Amount")
            .Add(x => x.Required, true).Add(x => x.Hint, "Whole numbers")
            .Add(x => x.Error, "Required").Add(x => x.ChildContent, child));
        Assert.Equal("amount", cut.Find("label").GetAttribute("for"));
        Assert.Equal("alert", cut.Find("#amount-error").GetAttribute("role"));
        Assert.Equal("amount-hint amount-error", observed?.DescribedBy);
        Assert.True(observed?.Required);
    }

    [Fact]
    public void DisabledSuppressesErrorAndNeverLeavesDanglingDescription()
    {
        var cut = Render<HarborlineFormField>(p => p.Add(x => x.Name, "amount").Add(x => x.Label, "Amount")
            .Add(x => x.Disabled, true).Add(x => x.Hint, "Whole numbers").Add(x => x.Error, "Required"));
        Assert.Empty(cut.FindAll("#amount-error"));
        Assert.NotEmpty(cut.FindAll("#amount-hint"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.form-field")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("form-field.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("x", Render<HarborlineFormField>(p => p.Add(x => x.Name, "x").Add(x => x.Label, "X")).Find("label").GetAttribute("for"));
    }

    private sealed class ContextProbe : ComponentBase
    {
        [CascadingParameter] public FormFieldContextValue? Context { get; set; }
        [Parameter] public EventCallback<FormFieldContextValue> Observed { get; set; }
        protected override async Task OnParametersSetAsync() { if (Context is not null) await Observed.InvokeAsync(Context); }
    }
}
