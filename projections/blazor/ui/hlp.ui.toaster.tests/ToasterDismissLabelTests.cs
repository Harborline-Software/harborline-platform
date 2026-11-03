using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: DismissLabel is caller-overridable or locale-catalog-backed and supplies the close
// control's accessible name. Null means the current localized catalog value (key
// common.dismissNotification); explicit caller text overrides it; the name is never empty.
public sealed class ToasterDismissLabelTests : BunitContext
{
    private readonly ToastService service = new();
    public ToasterDismissLabelTests() => Services.AddSingleton<IToastService>(service);

    private static readonly Dictionary<string, string> French = new() { ["common.dismissNotification"] = "Fermer la notification" };
    private static readonly Dictionary<string, string> Arabic = new() { ["common.dismissNotification"] = "إغلاق الإشعار" };

    private static string CloseName<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var name = cut.WaitForElement(".hl-toaster__close", TimeSpan.FromSeconds(2)).GetAttribute("aria-label") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(name));
        return name;
    }

    private IRenderedComponent<HarborlineLocaleProvider> Localized(string locale, IReadOnlyDictionary<string, string> catalog, string? dismissLabel = null) =>
        Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).Add(x => x.Catalog, catalog)
            .AddChildContent<HarborlineToaster>(c => c.Add(x => x.DismissLabel, dismissLabel)));

    [Fact]
    public void UsesTheDefaultCatalogValueWithoutAProviderOrCallerText()
    {
        var cut = Render<HarborlineToaster>();
        service.Error("Upload failed");
        Assert.Equal("Dismiss notification", CloseName(cut));
    }

    [Fact]
    public void UsesTheHostCatalogValueWhenTheCallerSuppliesNone()
    {
        var cut = Localized("fr-FR", French);
        service.Error("Upload failed");
        Assert.Equal("Fermer la notification", CloseName(cut));
    }

    [Fact]
    public void ExplicitCallerTextOverridesTheCatalog()
    {
        var cut = Localized("fr-FR", French, "Close upload alert");
        service.Error("Upload failed");
        Assert.Equal("Close upload alert", CloseName(cut));
    }

    [Fact]
    public void FollowsALocaleAndCatalogChangeWhileTheToastIsShown()
    {
        var cut = Localized("fr-FR", French);
        service.Error("Upload failed");
        Assert.Equal("Fermer la notification", CloseName(cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, Arabic));
        Assert.Equal("إغلاق الإشعار", CloseName(cut));
    }

    [Fact]
    public void BlankCallerTextNeverRendersAnEmptyName()
    {
        var cut = Localized("fr-FR", French, "   ");
        service.Error("Upload failed");
        Assert.Equal("Fermer la notification", CloseName(cut));
    }

    [Fact]
    public void UsesAPseudoLocalizedCatalogValueUnderEnXA()
    {
        const string pseudo = "⟦ Đîšmîšš ñøţîƒîçåţîøñ ···· ⟧";
        var cut = Localized("en-XA", new Dictionary<string, string> { ["common.dismissNotification"] = pseudo });
        service.Error("Upload failed");
        Assert.Equal(pseudo, CloseName(cut));
    }

    [Fact]
    public void FallsBackToTheDefaultCatalogValueWhenTheHostValueIsBlank()
    {
        var cut = Localized("fr-FR", new Dictionary<string, string> { ["common.dismissNotification"] = "  " });
        service.Error("Upload failed");
        Assert.Equal("Dismiss notification", CloseName(cut));
    }
}
