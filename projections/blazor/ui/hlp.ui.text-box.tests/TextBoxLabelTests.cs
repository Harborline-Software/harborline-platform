using Bunit;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: clear and reveal labels follow instance, catalog, then fallback precedence, and every
// action has a nonempty accessible name.
public sealed class TextBoxLabelTests : BunitContext
{
    private static readonly Dictionary<string, string> French = new() { ["common.clear"] = "Effacer", ["forms.textBox.showPassword"] = "Afficher le mot de passe", ["forms.textBox.hidePassword"] = "Masquer le mot de passe" };
    private static readonly Dictionary<string, string> Arabic = new() { ["common.clear"] = "امسح", ["forms.textBox.showPassword"] = "اعرض كلمة المرور", ["forms.textBox.hidePassword"] = "أخفِ كلمة المرور" };

    public TextBoxLabelTests() => JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/text-box.js").Mode = JSRuntimeMode.Loose;

    private static (string Clear, string Reveal) Names<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var clear = cut.Find("[data-hl-action=clear]").GetAttribute("aria-label") ?? string.Empty;
        var reveal = cut.Find("[data-hl-action=reveal]").GetAttribute("aria-label") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(clear));
        Assert.False(string.IsNullOrWhiteSpace(reveal));
        return (clear, reveal);
    }

    private IRenderedComponent<HarborlineLocaleProvider> Localized(string locale, IReadOnlyDictionary<string, string> catalog, string? clearLabel = null, string? showLabel = null) =>
        Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).Add(x => x.Catalog, catalog).AddChildContent<HarborlineTextBox>(c => c
            .Add(x => x.AccessibleName, "Secret").Add(x => x.ClearButton, true).Add(x => x.ShowReveal, true).Add(x => x.Type, "password").Add(x => x.DefaultValue, "hunter2")
            .Add(x => x.ClearLabel, clearLabel).Add(x => x.ShowPasswordLabel, showLabel)));

    [Fact]
    public void UsesTheDefaultCatalogWithoutAProviderAndTheHideLabelOnceRevealed()
    {
        var cut = Render<HarborlineTextBox>(p => p.Add(x => x.AccessibleName, "Secret").Add(x => x.ClearButton, true).Add(x => x.ShowReveal, true).Add(x => x.Type, "password").Add(x => x.DefaultValue, "hunter2"));
        Assert.Equal(("Clear", "Show password"), Names(cut));
        cut.Find("[data-hl-action=reveal]").Click();
        Assert.Equal("Hide password", Names(cut).Reveal);
    }

    [Fact]
    public void UsesTheHostCatalogAndCallerTextOverridesIt()
    {
        Assert.Equal(("Effacer", "Afficher le mot de passe"), Names(Localized("fr-FR", French)));
        Assert.Equal(("Vider", "Voir"), Names(Localized("fr-FR", French, "Vider", "Voir")));
    }

    [Fact]
    public void FollowsALocaleAndCatalogChange()
    {
        var cut = Localized("fr-FR", French);
        Assert.Equal("Effacer", Names(cut).Clear);
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, Arabic));
        Assert.Equal(("امسح", "اعرض كلمة المرور"), Names(cut));
        Assert.Equal("rtl", cut.Find("input").GetAttribute("dir"));
    }

    [Fact]
    public void UsesAPseudoLocalizedCatalogUnderEnXA()
    {
        const string pseudo = "⟦ Çļëåŗ ··· ⟧";
        Assert.Equal(pseudo, Names(Localized("en-XA", new Dictionary<string, string> { ["common.clear"] = pseudo })).Clear);
    }

    [Fact]
    public void NeverRendersAnEmptyNameForBlankCallerTextOrABlankCatalogValue()
    {
        Assert.Equal(("Effacer", "Afficher le mot de passe"), Names(Localized("fr-FR", French, "", "   ")));
        Assert.Equal("Clear", Names(Localized("fr-FR", new Dictionary<string, string> { ["common.clear"] = "  " })).Clear);
    }

    // A blank caller name is no name (WCAG 4.1.2): the FormField label still names the input, as Switch does.
    [Theory]
    [InlineData(" ")]
    [InlineData("\t\n")]
    [InlineData("")]
    public void IgnoresABlankAccessibleNameSoTheFormFieldLabelNamesTheInput(string name)
    {
        var field = new Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue("email", "email-label", null, false, false);
        var box = Render<Microsoft.AspNetCore.Components.CascadingValue<Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue>>(p => p.Add(x => x.Value, field).AddChildContent<HarborlineTextBox>(c => c.Add(x => x.AccessibleName, name))).Find("input");
        Assert.Equal((null, "email-label"), (box.GetAttribute("aria-label"), box.GetAttribute("aria-labelledby")));
    }

    [Fact]
    public void KeepsANonblankAccessibleNameExactlyOverTheFormFieldLabel()
    {
        var field = new Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue("email", "email-label", null, false, false);
        var box = Render<Microsoft.AspNetCore.Components.CascadingValue<Harborline.UIAdapters.Blazor.Components.Forms.FormFieldContextValue>>(p => p.Add(x => x.Value, field).AddChildContent<HarborlineTextBox>(c => c.Add(x => x.AccessibleName, " Work email "))).Find("input");
        Assert.Equal((" Work email ", null), (box.GetAttribute("aria-label"), box.GetAttribute("aria-labelledby")));
    }
}
