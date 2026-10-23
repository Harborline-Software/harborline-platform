using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: the switch always has a nonempty accessible name, and its on and off state copy follows
// instance, catalog, then fallback precedence.
public sealed class SwitchFieldLabelTests : BunitContext
{
    private static readonly Dictionary<string, string> French = new() { ["forms.switch.on"] = "Activé", ["forms.switch.off"] = "Désactivé" };
    private static readonly Dictionary<string, string> Arabic = new() { ["forms.switch.on"] = "تشغيل", ["forms.switch.off"] = "إيقاف" };

    private static string State<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => cut.Find(".hl-switch__state").TextContent;
    private static string Name<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var control = cut.Find("[role=switch]");
        var labelledBy = control.GetAttribute("aria-labelledby");
        var name = (labelledBy is not null ? string.Join(" ", labelledBy.Split(' ').Select(id => cut.Find($"#{id}").TextContent)) : control.GetAttribute("aria-label") ?? string.Empty).Trim();
        Assert.False(string.IsNullOrWhiteSpace(name));
        return name;
    }

    private IRenderedComponent<HarborlineLocaleProvider> Localized(string locale, IReadOnlyDictionary<string, string> catalog, bool isChecked = false, string? offText = null, string? onText = null) =>
        Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).Add(x => x.Catalog, catalog).AddChildContent<HarborlineSwitchField>(c => c
            .Add(x => x.Name, "published").Add(x => x.Label, "Published").Add(x => x.Checked, isChecked).Add(x => x.CheckedChanged, _ => { }).Add(x => x.OffText, offText).Add(x => x.OnText, onText)));

    [Fact]
    public void UsesTheFallbackStateCopyWithoutAProviderAndANameFromTheLabel()
    {
        var cut = Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "published").Add(x => x.Label, "Published").Add(x => x.CheckedChanged, _ => { }));
        Assert.Equal("Published", Name(cut));
        Assert.Equal("Off", State(cut));
    }

    [Fact]
    public void UsesTheHostCatalogAndCallerTextOverridesIt()
    {
        Assert.Equal("Activé", State(Localized("fr-FR", French, isChecked: true)));
        Assert.Equal("Oui", State(Localized("fr-FR", French, isChecked: true, onText: "Oui")));
    }

    [Fact]
    public void FollowsALocaleAndCatalogChange()
    {
        var cut = Localized("fr-FR", French);
        Assert.Equal("Désactivé", State(cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, Arabic));
        Assert.Equal("إيقاف", State(cut));
        Assert.Equal("rtl", cut.Find(".hl-switch").GetAttribute("dir"));
        Assert.Equal("Published", Name(cut));
    }

    [Fact]
    public void UsesAPseudoLocalizedCatalogUnderEnXA()
    {
        const string pseudo = "⟦ Öƒƒ ··· ⟧";
        Assert.Equal(pseudo, State(Localized("en-XA", new Dictionary<string, string> { ["forms.switch.off"] = pseudo })));
    }

    [Fact]
    public void NeverRendersBlankStateCopyForBlankCallerTextOrABlankCatalogValue()
    {
        Assert.Equal("Désactivé", State(Localized("fr-FR", French, offText: "  ")));
        Assert.Equal("Off", State(Localized("fr-FR", new Dictionary<string, string> { ["forms.switch.off"] = " " })));
    }

    [Fact]
    public void RejectsABlankAccessibleNameRatherThanRenderingAnUnnamedSwitch()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<HarborlineSwitchField>(p => p.Add(x => x.Name, "published").Add(x => x.AccessibleName, "   ").Add(x => x.CheckedChanged, _ => { })));
        Assert.Equal("accessible-switch-label-required", error.Message);
    }
}
