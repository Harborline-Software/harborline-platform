using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SelectFieldTests : BunitContext
{
    private static IReadOnlyList<SelectOption> Options=>[new("active","Active"),new("pending","Pending")];
    public SelectFieldTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleAcknowledgmentRetainsActiveOptionAndDraft(bool searchable)
    {
        var changes = new List<IReadOnlyList<string>>();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Multiple, true)
            .Add(x => x.Searchable, searchable).Add(x => x.Values, new[] { "active" })
            .Add(x => x.Options, Options).Add(x => x.ValuesChanged, value => changes.Add(value)));
        cut.Find("button").Click();
        if (searchable)
        {
            cut.Find("input").Input("pen");
            cut.Find("input").KeyDown("ArrowDown");
        }
        else cut.Find("[role=listbox]").KeyDown("End");
        cut.Find("[role=listbox]").KeyDown(" ");
        Assert.Equal(["active", "pending"], Assert.Single(changes));
        cut.Render(p => p.Add(x => x.Values, new[] { "active", "pending" }).Add(x => x.Options, Options));
        Assert.Equal(cut.Find("[data-hl-option=pending]").Id, cut.Find("[role=listbox]").GetAttribute("aria-activedescendant"));
        if (searchable) Assert.Equal("pen", cut.Find("input").GetAttribute("value"));
        cut.Find("[role=listbox]").KeyDown(" ");
        Assert.Equal(2, changes.Count);
        Assert.Equal(["active"], changes[1]);
        cut.Render(p => p.Add(x => x.Values, new[] { "active" }).Add(x => x.Options, Options));
        Assert.Equal("false", cut.Find("[data-hl-option=pending]").GetAttribute("aria-selected"));
        cut.Render(p => p.Add(x => x.Values, new[] { "pending" }).Add(x => x.Options, Options));
        Assert.False(cut.Find("[role=listbox]").HasAttribute("aria-activedescendant"));
        if (searchable) Assert.Equal("", cut.Find("input").GetAttribute("value"));
        cut.Find("[role=listbox]").KeyDown(" ");
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ClosedArrowDownOpensOnSelectionWithoutAdvancing()
    {
        var changes = new List<string>();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Value, "active")
            .Add(x => x.Options, Options).Add(x => x.ValueChanged, value => changes.Add(value)));
        cut.Find("button").KeyDown("ArrowDown");
        Assert.Equal("true", cut.Find("[data-hl-option=active]").GetAttribute("data-hl-active"));
        Assert.Empty(changes);
        cut.Find("[role=listbox]").KeyDown("Enter");
        Assert.Equal(["active"], changes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleOpeningActivatesSelectedEnabledMemberWithoutToggling(bool searchable)
    {
        var changes = new List<IReadOnlyList<string>>();
        var choices = new SelectOption[] { new("active", "Active"), new("blocked", "Blocked", true), new("pending", "Pending") };
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Multiple, true)
            .Add(x => x.Searchable, searchable).Add(x => x.Values, new[] { "blocked", "pending" })
            .Add(x => x.Options, choices).Add(x => x.ValuesChanged, value => changes.Add(value)));
        cut.Find("button").Click();
        Assert.Equal(3, cut.FindAll("[role=option]").Count);
        var selectedId = cut.Find("[data-hl-option=pending]").Id;
        Assert.Equal(selectedId, cut.Find("[role=listbox]").GetAttribute("aria-activedescendant"));
        if (searchable)
        {
            Assert.Equal("", cut.Find("input").GetAttribute("value"));
            cut.Find("input").KeyDown("ArrowDown");
            Assert.Equal(selectedId, cut.Find("[role=listbox]").GetAttribute("aria-activedescendant"));
        }
        Assert.Empty(changes);
        cut.Find("[role=listbox]").KeyDown("Escape");
        cut.Render(p => p.Add(x => x.Values, new[] { "blocked", "unknown" }));
        cut.Find("button").Click();
        Assert.Equal(cut.Find("[data-hl-option=active]").Id, cut.Find("[role=listbox]").GetAttribute("aria-activedescendant"));
        Assert.Empty(changes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleValidationStatesBelongToListbox(bool searchable)
    {
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.AccessibleName, "Status")
            .Add(x => x.Multiple, true).Add(x => x.Searchable, searchable).Add(x => x.Open, true)
            .Add(x => x.Required, true).Add(x => x.ReadOnly, true).Add(x => x.Error, true)
            .Add(x => x.Options, Options).Add(x => x.AdditionalAttributes, new Dictionary<string, object> { ["aria-describedby"] = "hint error" }));
        var trigger = cut.Find("button");
        var list = cut.Find("[role=listbox]");
        Assert.False(trigger.HasAttribute("aria-required"));
        Assert.False(trigger.HasAttribute("aria-readonly"));
        Assert.Equal("Status", trigger.GetAttribute("aria-label"));
        Assert.Equal("hint error", trigger.GetAttribute("aria-describedby"));
        Assert.Equal("true", trigger.GetAttribute("aria-invalid"));
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.Equal(list.Id, trigger.GetAttribute("aria-controls"));
        Assert.Equal("Status", list.GetAttribute("aria-label"));
        Assert.Equal("true", list.GetAttribute("aria-required"));
        Assert.Equal("true", list.GetAttribute("aria-readonly"));
    }
    [Fact]
    public void ExternalPopupDismissalDiscardsDraftBeforeReopening()
    {
        var changes = new List<string>();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Searchable, true)
            .Add(x => x.Open, true).Add(x => x.Value, "active").Add(x => x.Options, Options).Add(x => x.ValueChanged, value => changes.Add(value)));
        cut.Find("input").Input("pen");
        cut.Render(p => p.Add(x => x.Open, false));
        cut.Render(p => p.Add(x => x.Open, true));
        Assert.Equal("Active", cut.Find("input").GetAttribute("value"));
        Assert.Empty(changes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleUsesButtonNamedListboxAndSeparateSearch(bool searchable)
    {
        var original = new[] { "unknown", "active" };
        var changes = new List<IReadOnlyList<string>>();
        var singles = new List<string>();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.AccessibleName, "Status")
            .Add(x => x.Multiple, true).Add(x => x.Searchable, searchable).Add(x => x.Values, original)
            .Add(x => x.Options, Options).Add(x => x.ValuesChanged, value => changes.Add(value)).Add(x => x.ValueChanged, value => singles.Add(value)));
        Assert.Empty(cut.FindAll("[role=combobox]"));
        var trigger = cut.Find("button");
        Assert.Equal("listbox", trigger.GetAttribute("aria-haspopup"));
        trigger.Click();
        var list = cut.Find("[role=listbox]");
        Assert.Equal("Status", list.GetAttribute("aria-label"));
        Assert.Equal("-1", list.GetAttribute("tabindex"));
        Assert.Equal("true", list.GetAttribute("aria-multiselectable"));
        if (searchable)
        {
            Assert.Empty(list.QuerySelectorAll("input"));
            var search = cut.Find("[role=searchbox]");
            Assert.Equal(2, cut.FindAll("[role=option]").Count);
            search.Input("PEN"); search.KeyDown("Enter"); Assert.Empty(changes);
            search.KeyDown("ArrowDown");
        }
        else list.KeyDown("End");
        list.KeyDown(" ");
        Assert.Equal(["unknown", "active", "pending"], Assert.Single(changes));
        Assert.Empty(singles);
        Assert.Equal(["unknown", "active"], original);
        Assert.Single(cut.FindAll("[role=listbox]"));
        Assert.Equal("false", cut.Find("[data-hl-option=pending]").GetAttribute("aria-selected"));
        list.KeyDown("Escape"); Assert.Empty(cut.FindAll("[role=listbox]"));
        trigger.Click();
        cut.Find(searchable ? "[role=searchbox]" : "[role=listbox]").KeyDown("Tab");
        Assert.Empty(cut.FindAll("[role=listbox]"));
    }
    [Fact]
    public async Task SearchBoundsFortyThousandAndRefusesFreeTextAndStaleMembership()
    {
        var values = new List<string>();
        var many = Enumerable.Range(0, 40000).Select(index => new SelectOption(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Member " + index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "members").Add(x => x.Searchable, true)
            .Add(x => x.Options, many).Add(x => x.ValueChanged, value => values.Add(value)));
        var input = cut.Find("input"); input.Focus();
        Assert.Equal(25, cut.FindAll("[role=option]").Count);
        input.Input("MEMBER 3999"); Assert.Equal(10, cut.FindAll("[role=option]").Count);
        input.KeyDown("Enter"); Assert.Empty(values);
        input.Input(".*"); Assert.Empty(cut.FindAll("[role=option]"));
        input.KeyDown("ArrowDown"); input.KeyDown("Enter"); Assert.Empty(values);
        input.Input("MEMBER 3999"); input.KeyDown("ArrowDown");
        cut.Render(p => p.Add(x => x.Options, Options).Add(x => x.Value, "active"));
        Assert.Equal("Active", cut.Find("input").GetAttribute("value"));
        input.KeyDown("Enter"); Assert.Empty(values);
        foreach (var key in new[] { "Escape", "Tab" }) {
            input.Input("pen"); input.KeyDown(key);
            Assert.Equal("Active", cut.Find("input").GetAttribute("value"));
        }
        input.Input("pen");
        await cut.InvokeAsync(cut.Instance.OnOutsidePointer);
        Assert.Empty(cut.FindAll("[role=listbox]"));
        Assert.Equal("Active", cut.Find("input").GetAttribute("value"));
        Assert.Empty(values);
    }
    [Fact]
    public void MultipleRefusesDisabledAndReadOnlyAndClearsLastExplicitValue()
    {
        var changes = new List<IReadOnlyList<string>>();
        var choices = new SelectOption[] { new("active", "Active"), new("blocked", "Blocked", true) };
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Multiple, true)
            .Add(x => x.Values, new[] { "active" }).Add(x => x.Options, choices).Add(x => x.ValuesChanged, value => changes.Add(value)));
        cut.Find("button").Click();
        cut.Find("[data-hl-option=blocked]").Click(); Assert.Empty(changes);
        cut.Find("[data-hl-option=active]").Click(); Assert.Empty(Assert.Single(changes));
        cut.Render(p => p.Add(x => x.ReadOnly, true));
        cut.Find("[data-hl-option=active]").Click(); Assert.Single(changes);
        cut.Render(p => p.Add(x => x.Disabled, true));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        cut.Find("[data-hl-option=active]").Click(); Assert.Single(changes);
    }
    [Fact]
    public void InvalidBoundsAndDuplicateValuesRefuse()
    {
        Assert.Throws<InvalidOperationException>(() => Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.MaxVisibleOptions, 0)));
        Assert.Throws<InvalidOperationException>(() => Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Multiple, true).Add(x => x.Values, new[] { "a", "a" })));
    }
    [Fact] public void SearchDraftOnlyCommitsCurrentMatch()
    {
        var values = new List<string>();
        var cut = Render<HarborlineSelectField>(p => p.Add(x => x.Name, "status").Add(x => x.Value, "active")
            .Add(x => x.Options, Options).Add(x => x.AccessibleName, "Status")
            .Add(x => x.Searchable, true).Add(x => x.ValueChanged, value => values.Add(value)));
        var input = cut.Find("input[role=combobox]");
        input.Input("PEN");
        Assert.Single(cut.FindAll("[role=option]"));
        Assert.Empty(values);
        input.KeyDown("ArrowDown"); input.KeyDown("Enter");
        Assert.Equal(["pending"], values);
        Assert.Equal("Active", cut.Find("input").GetAttribute("value"));
    }
    [Fact] public void RendersControlledComboboxAndListbox(){var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"active").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status"));Assert.Equal("Active",cut.Find(".hl-select-field__value").TextContent);cut.Find("[role=combobox]").Click();Assert.Equal("true",cut.Find("[role=combobox]").GetAttribute("aria-expanded"));Assert.Equal(2,cut.FindAll("[role=option]").Count);}
    [Fact] public void SelectionRequestsValueAndCloseOnce(){var values=new List<string>();var opens=new List<bool>();var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"active").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status").Add(x=>x.ValueChanged,v=>values.Add(v)).Add(x=>x.OpenChanged,v=>opens.Add(v)));cut.Find("[role=combobox]").Click();cut.Find("[data-hl-option=pending]").Click();Assert.Equal(["pending"],values);Assert.Equal([true,false],opens);}
    [Fact] public void ControlledOpenRequestsWithoutChangingRenderedState(){var opens=new List<bool>();var cut=Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status").Add(x=>x.Open,false).Add(x=>x.OpenChanged,v=>opens.Add(v)));cut.Find("button").Click();Assert.Equal([true],opens);Assert.Equal("false",cut.Find("button").GetAttribute("aria-expanded"));}
    [Fact,Trait("ModuleConformance","hlp.ui.select-field")] public void SharedFixtureConforms(){AssertFixturePrefix("select-field.");Assert.NotNull(Render<HarborlineSelectField>(p=>p.Add(x=>x.Name,"status").Add(x=>x.Value,"").Add(x=>x.Options,Options).Add(x=>x.AccessibleName,"Status")));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
