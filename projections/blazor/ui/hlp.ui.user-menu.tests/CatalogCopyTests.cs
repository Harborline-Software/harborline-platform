using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Localization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Forms.Inputs;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Harborline.UIAdapters.Blazor.Localization;
using Harborline.UIAdapters.Blazor.Shell;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: UserMenu, NotificationCenter and Switch take their default copy from the catalog
// (the host's, else the default catalog); caller text overrides it; a catalog change, Arabic and en-XA
// are followed; and no name is ever empty.
public sealed class CatalogCopyTests : BunitContext
{
    public CatalogCopyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IOutsidePointerObserver>(new CapturingOutside());
    }

    private IRenderedComponent<HarborlineLocaleProvider> Localized(string locale, IReadOnlyDictionary<string, string> catalog, RenderFragment child) =>
        Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, locale).Add(x => x.Catalog, catalog).AddChildContent(child));
    private static string NonEmpty(string? value) { Assert.False(string.IsNullOrWhiteSpace(value)); return value!; }

    // ── UserMenu ──
    private static RenderFragment Menu(UserMenuLabels? labels = null) => builder =>
    {
        builder.OpenComponent<HarborlineUserMenu>(0);
        builder.AddComponentParameter(1, nameof(HarborlineUserMenu.Identity), new UserIdentity("Avery"));
        builder.AddComponentParameter(2, nameof(HarborlineUserMenu.OnSignOut), EventCallback.Factory.Create(new object(), () => { }));
        if (labels is not null) builder.AddComponentParameter(3, nameof(HarborlineUserMenu.Labels), labels);
        builder.CloseComponent();
    };
    private static (string Trigger, string Panel, string SignOut) MenuNames(IRenderedComponent<IComponent> cut)
    {
        cut.Find(".hl-user-menu__trigger").Click();
        var names = (NonEmpty(cut.Find(".hl-user-menu__trigger").GetAttribute("aria-label")), NonEmpty(cut.Find(".hl-user-menu__surface").GetAttribute("aria-label")), NonEmpty(cut.Find(".hl-user-menu__sign-out").TextContent));
        cut.Find(".hl-user-menu__trigger").Click();
        return names;
    }

    [Fact]
    public void UserMenuTakesItsDefaultsFromTheDefaultCatalogWithoutAProvider()
    {
        var cut = (IRenderedComponent<IComponent>)Render(Menu());
        Assert.Equal((HarborlineDefaultStrings.Get("navigation.userMenu.trigger").Replace("{name}", "Avery", StringComparison.Ordinal), HarborlineDefaultStrings.Get("navigation.userMenu.panel"), HarborlineDefaultStrings.Get("navigation.userMenu.signOut")), MenuNames(cut));
    }

    [Fact]
    public void UserMenuFollowsTheHostCatalogAndCallerTextOverridesIt()
    {
        var french = new Dictionary<string, string> { ["navigation.userMenu.trigger"] = "Menu du compte de {name}", ["navigation.userMenu.panel"] = "Menu du compte", ["navigation.userMenu.signOut"] = "Se déconnecter" };
        var arabic = new Dictionary<string, string> { ["navigation.userMenu.trigger"] = "قائمة حساب {name}", ["navigation.userMenu.panel"] = "قائمة الحساب", ["navigation.userMenu.signOut"] = "تسجيل الخروج" };
        var cut = Localized("fr-FR", french, Menu());
        Assert.Equal(("Menu du compte de Avery", "Menu du compte", "Se déconnecter"), MenuNames((IRenderedComponent<IComponent>)(object)cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, arabic));
        Assert.Equal(("قائمة حساب Avery", "قائمة الحساب", "تسجيل الخروج"), MenuNames((IRenderedComponent<IComponent>)(object)cut));
        // A supplied record is caller text: kept exactly, even where it repeats the English default.
        var overridden = Localized("ar-SA", arabic, Menu(UserMenuLabels.English with { SignOut = "Leave" }));
        Assert.Equal(("Account menu for Avery", "Account menu", "Leave"), MenuNames((IRenderedComponent<IComponent>)(object)overridden));
        // Blank caller text is no text: that field alone falls back to the catalog.
        var blank = Localized("ar-SA", arabic, Menu(UserMenuLabels.English with { Panel = " " }));
        Assert.Equal(("Account menu for Avery", "قائمة الحساب", "Sign out"), MenuNames((IRenderedComponent<IComponent>)(object)blank));
        Assert.Equal("⟦ Šîĝñ øûţ ··· ⟧", MenuNames((IRenderedComponent<IComponent>)(object)Localized("en-XA", new Dictionary<string, string> { ["navigation.userMenu.signOut"] = "⟦ Šîĝñ øûţ ··· ⟧" }, Menu())).SignOut);
        Assert.Equal("Account menu", MenuNames((IRenderedComponent<IComponent>)(object)Localized("fr-FR", new Dictionary<string, string> { ["navigation.userMenu.panel"] = "  " }, Menu())).Panel);
    }

    // ── NotificationCenter ──
    private static RenderFragment Center(NotificationCenterLabels? labels = null) => builder =>
    {
        builder.OpenComponent<HarborlineNotificationCenter>(0);
        builder.AddComponentParameter(1, nameof(HarborlineNotificationCenter.Items), new NotificationCenterItem[] { new("p1", "Approve export", "now", "proposal"), new("r1", "Export ready", "later", "result") });
        builder.AddComponentParameter(2, nameof(HarborlineNotificationCenter.OnDismiss), EventCallback.Factory.Create<NotificationCenterItem>(new object(), _ => { }));
        if (labels is not null) builder.AddComponentParameter(3, nameof(HarborlineNotificationCenter.Labels), labels);
        builder.CloseComponent();
    };
    private static (string Dialog, string Tablist, string Dismiss) CenterNames(IRenderedComponent<IComponent> cut)
    {
        cut.Find(".hl-notification-center__trigger").Click();
        var names = (NonEmpty(cut.Find("[role=dialog]").GetAttribute("aria-label")), NonEmpty(cut.Find("[role=tablist]").GetAttribute("aria-label")), NonEmpty(cut.Find(".hl-notification-center__dismiss").GetAttribute("aria-label")));
        cut.Find(".hl-notification-center__trigger").Click();
        return names;
    }

    [Fact]
    public void NotificationCenterTakesItsDefaultsFromTheCatalogAndHonoursGroupLabel()
    {
        var plain = (IRenderedComponent<IComponent>)Render(Center());
        Assert.Equal((HarborlineDefaultStrings.Get("navigation.notifications.title"), HarborlineDefaultStrings.Get("navigation.notifications.groups"), HarborlineDefaultStrings.Get("navigation.notifications.dismiss").Replace("{title}", "Export ready", StringComparison.Ordinal)), CenterNames(plain));
        var french = new Dictionary<string, string> { ["navigation.notifications.title"] = "Avis", ["navigation.notifications.groups"] = "Groupes d'avis", ["navigation.notifications.dismiss"] = "Ignorer : {title}" };
        var arabic = new Dictionary<string, string> { ["navigation.notifications.title"] = "الإشعارات", ["navigation.notifications.groups"] = "مجموعات الإشعارات", ["navigation.notifications.dismiss"] = "تجاهل: {title}" };
        var cut = Localized("fr-FR", french, Center());
        Assert.Equal(("Avis", "Groupes d'avis", "Ignorer : Export ready"), CenterNames((IRenderedComponent<IComponent>)(object)cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, arabic));
        Assert.Equal(("الإشعارات", "مجموعات الإشعارات", "تجاهل: Export ready"), CenterNames((IRenderedComponent<IComponent>)(object)cut));
        // A supplied record is caller text, kept exactly; its unset GroupLabel and blank Title come from the catalog.
        Assert.Equal(("Notifications", "Kinds", "Dismiss: Export ready"), CenterNames((IRenderedComponent<IComponent>)(object)Localized("ar-SA", arabic, Center(NotificationCenterLabels.English with { GroupLabel = "Kinds" }))));
        Assert.Equal(("الإشعارات", "مجموعات الإشعارات", "Dismiss: Export ready"), CenterNames((IRenderedComponent<IComponent>)(object)Localized("ar-SA", arabic, Center(NotificationCenterLabels.English with { Title = " " }))));
        Assert.Equal("⟦ Ñøţîƒîçåţîøñš ··· ⟧", CenterNames((IRenderedComponent<IComponent>)(object)Localized("en-XA", new Dictionary<string, string> { ["navigation.notifications.title"] = "⟦ Ñøţîƒîçåţîøñš ··· ⟧" }, Center())).Dialog);
        Assert.Equal("Notification groups", CenterNames((IRenderedComponent<IComponent>)(object)Localized("fr-FR", new Dictionary<string, string> { ["navigation.notifications.groups"] = " " }, Center())).Tablist);
    }

    [Fact]
    public void NotificationCenterNamesTheBellFromTheCatalogAcrossLocales()
    {
        string BellName(IRenderedComponent<HarborlineLocaleProvider> cut) => NonEmpty(cut.Find(".hl-notification-center__trigger").GetAttribute("aria-label"));
        var french = new Dictionary<string, string> { ["navigation.notifications.title"] = "Avis", ["navigation.notifications.bell.unread.one"] = "{title}, {count} non lue", ["navigation.notifications.bell.unread.other"] = "{title}, {count} non lues" };
        var arabic = new Dictionary<string, string> { ["navigation.notifications.title"] = "الإشعارات", ["navigation.notifications.bell.unread.two"] = "{title}، إشعاران غير مقروءين", ["navigation.notifications.bell.unread.other"] = "{title}، {count} غير مقروء" };
        var cut = Localized("fr-FR", french, Center());
        Assert.Equal("Avis, 2 non lues", BellName(cut));
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, arabic));
        Assert.Equal("الإشعارات، إشعاران غير مقروءين", BellName(cut));
        cut.Render(p => p.Add(x => x.Locale, "en-XA").Add(x => x.Catalog, new Dictionary<string, string> { ["navigation.notifications.bell.unread.other"] = "⟦ {title}, {count} ûñŕéåð ··· ⟧" }));
        Assert.Equal("⟦ Notifications, 2 ûñŕéåð ··· ⟧", BellName(cut));
    }

    // ── Switch ──
    [Fact]
    public void SwitchTakesItsStateCopyFromTheDefaultCatalogWithoutAProvider()
    {
        var on = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Published").Add(x => x.Checked, true));
        Assert.Equal(HarborlineDefaultStrings.Get("forms.switch.on"), on.Find(".hl-switch__state").TextContent);
        var off = Render<HarborlineSwitch>(p => p.Add(x => x.AccessibleName, "Published").Add(x => x.Checked, false));
        Assert.Equal(HarborlineDefaultStrings.Get("forms.switch.off"), off.Find(".hl-switch__state").TextContent);
    }
}
