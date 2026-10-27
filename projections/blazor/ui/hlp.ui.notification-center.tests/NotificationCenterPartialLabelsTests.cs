using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract (interface revision 3, G6): in a supplied NotificationCenterLabels record each null or blank field comes
// from the catalog (host, then default) and every other field is kept exactly. The unsupplied default record (the
// English instance itself) takes everything from the catalog, and a fully specified record, including a copy of
// English, renders as before.
public sealed class NotificationCenterPartialLabelsTests : BunitContext
{
    private static readonly Dictionary<string, string> French = new()
    {
        ["navigation.notifications.title"] = "Avis", ["navigation.notifications.all"] = "Tout", ["navigation.notifications.confirm"] = "Confirmer",
        ["navigation.notifications.deny"] = "Refuser", ["navigation.notifications.markAllRead"] = "Tout marquer comme lu", ["navigation.notifications.viewAll"] = "Tout voir",
        ["navigation.notifications.empty"] = "Aucun avis", ["navigation.notifications.groups"] = "Groupes d'avis", ["navigation.notifications.dismiss"] = "Ignorer : {title}",
        ["navigation.notifications.unread"] = "Non lu",
    };
    private static readonly Dictionary<string, string> Arabic = new()
    {
        ["navigation.notifications.title"] = "الإشعارات", ["navigation.notifications.all"] = "الكل", ["navigation.notifications.confirm"] = "تأكيد",
        ["navigation.notifications.deny"] = "رفض", ["navigation.notifications.markAllRead"] = "تعليم الكل كمقروء", ["navigation.notifications.viewAll"] = "عرض الكل",
        ["navigation.notifications.empty"] = "لا توجد إشعارات", ["navigation.notifications.groups"] = "مجموعات الإشعارات", ["navigation.notifications.dismiss"] = "تجاهل: {title}",
        ["navigation.notifications.unread"] = "غير مقروء",
    };
    private static readonly IReadOnlyDictionary<string, string?> ArabicChrome = new Dictionary<string, string?>
    {
        ["dialog"] = "الإشعارات", ["markAllRead"] = "تعليم الكل كمقروء", ["tablist"] = "مجموعات الإشعارات", ["all"] = "الكل", ["confirm"] = "تأكيد", ["deny"] = "رفض",
        ["unread"] = "غير مقروء", ["dismiss"] = "تجاهل: Export ready", ["viewAll"] = "عرض الكل", ["empty"] = "لا توجد إشعارات",
    };
    private static readonly IReadOnlyDictionary<string, string?> EnglishChrome = new Dictionary<string, string?>
    {
        ["dialog"] = "Notifications", ["markAllRead"] = "Mark all read", ["tablist"] = "Notification groups", ["all"] = "All", ["confirm"] = "Confirm", ["deny"] = "Deny",
        ["unread"] = "Unread", ["dismiss"] = "Dismiss: Export ready", ["viewAll"] = "View all", ["empty"] = "No notifications",
    };
    private static IReadOnlyDictionary<string, string?> With(IReadOnlyDictionary<string, string?> names, params (string Field, string Value)[] changes)
    {
        var copy = names.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (field, value) in changes) copy[field] = value;
        return copy;
    }

    public NotificationCenterPartialLabelsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IOutsidePointerObserver>(new CapturingOutside());
    }

    private IReadOnlyDictionary<string, string?> Names(string locale, IReadOnlyDictionary<string, string> catalog, NotificationCenterLabels? labels) =>
        NotificationCenterTests.AllChrome(this, locale, catalog, "Export ready", labels);

    [Fact]
    public void APartialRecordKeepsItsFieldAndTakesTheRestFromTheArabicCatalog()
    {
        Assert.Equal(With(ArabicChrome, ("confirm", "Approve")), Names("ar-SA", Arabic, new NotificationCenterLabels(Confirm: "Approve")));
        Assert.Equal(With(ArabicChrome, ("confirm", "Approve")), Names("ar-SA", Arabic, new NotificationCenterLabels { Confirm = "Approve" }));
        Assert.Equal(With(ArabicChrome, ("dismiss", "Hide Export ready")), Names("ar-SA", Arabic, new NotificationCenterLabels(Dismiss: title => $"Hide {title}")));
    }

    [Fact]
    public void AnOmittedOrBlankFieldFallsBackToTheCatalog()
    {
        // Title blank, Empty empty, a dismiss that returns blank text, the rest omitted: all from the catalog; Deny kept.
        Assert.Equal(With(ArabicChrome, ("deny", "Decline")), Names("ar-SA", Arabic, new NotificationCenterLabels(Title: " ", Deny: "Decline", Empty: string.Empty, Dismiss: _ => "\n")));
        Assert.Equal(ArabicChrome, Names("ar-SA", Arabic, new NotificationCenterLabels()));
    }

    [Fact]
    public void ALocaleChangeUpdatesTheFallbackFieldsAndKeepsTheExplicitOne()
    {
        var labels = new NotificationCenterLabels(Confirm: "Approve");
        var cut = Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, "fr-FR").Add(x => x.Catalog, French).AddChildContent<HarborlineNotificationCenter>(c => c
            .Add(x => x.Items, new NotificationCenterItem[] { new("p1", "Approve export", "now", "proposal"), new("r1", "Export ready", "later", "result") })
            .Add(x => x.OnDismiss, _ => { }).Add(x => x.Labels, labels)));
        (string? Dialog, string Confirm, string Deny, string? Dismiss) Read()
        {
            cut.Find(".hl-notification-center__trigger").Click();
            var actions = cut.Find(".hl-notification-center__actions").QuerySelectorAll("button");
            var names = (cut.Find("[role=dialog]").GetAttribute("aria-label"), actions[0].TextContent, actions[1].TextContent, cut.Find(".hl-notification-center__dismiss").GetAttribute("aria-label"));
            cut.Find(".hl-notification-center__trigger").Click();
            return names;
        }
        Assert.Equal(("Avis", "Approve", "Refuser", "Ignorer : Export ready"), Read());
        cut.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Catalog, Arabic));
        Assert.Equal(("الإشعارات", "Approve", "رفض", "تجاهل: Export ready"), Read());
    }

    [Fact]
    public void AnExplicitGroupLabelIsKept()
    {
        Assert.Equal(With(ArabicChrome, ("tablist", "Kinds")), Names("ar-SA", Arabic, new NotificationCenterLabels(GroupLabel: "Kinds")));
        Assert.Equal(With(EnglishChrome, ("tablist", "Kinds")), Names("ar-SA", Arabic, NotificationCenterLabels.English with { GroupLabel = "Kinds" }));
    }

    [Fact]
    public void AFullySpecifiedEnglishRecordStaysEnglishUnderAnArabicCatalog()
    {
        // English leaves GroupLabel unset, so a copy of it takes the group name from the catalog and keeps the rest.
        var englishCopy = With(EnglishChrome, ("tablist", "مجموعات الإشعارات"));
        Assert.Equal(englishCopy, Names("ar-SA", Arabic, NotificationCenterLabels.English with { }));
        Assert.Equal(EnglishChrome, Names("ar-SA", Arabic, new NotificationCenterLabels("Notifications", "All", "Confirm", "Deny", "Mark all read", "View all", "No notifications", title => $"Dismiss: {title}", "Unread", "Notification groups")));
        // Only the English instance itself means "unsupplied".
        Assert.Equal(ArabicChrome, Names("ar-SA", Arabic, NotificationCenterLabels.English));
        Assert.Equal(ArabicChrome, Names("ar-SA", Arabic, null));
    }

    [Fact]
    public void ExistingAndNewConstructionShapesCompileAndEnglishIsUnchanged()
    {
        NotificationCenterLabels positional = new("Inbox", "Everything", "Yes", "No", "Read all", "More", "Nothing", title => $"Hide {title}", "New");
        NotificationCenterLabels copy = NotificationCenterLabels.English with { };
        NotificationCenterLabels edited = NotificationCenterLabels.English with { Confirm = "Approve" };
        NotificationCenterLabels named = new(Confirm: "Approve");
        NotificationCenterLabels initialized = new() { Confirm = "Approve" };
        Assert.Equal(("Inbox", null), (positional.Title, positional.GroupLabel));
        Assert.Equal(("Notifications", "Confirm"), (copy.Title, copy.Confirm));
        Assert.Equal(("Notifications", "Approve"), (edited.Title, edited.Confirm));
        Assert.Equal((null, "Approve", null), (named.Title, named.Confirm, (object?)named.Dismiss));
        Assert.Equal(named, initialized);
        Assert.Same(NotificationCenterLabels.English, NotificationCenterLabels.English);
        Assert.Equal(("Notifications", "Dismiss: Ada", null), (NotificationCenterLabels.English.Title, NotificationCenterLabels.English.Dismiss!("Ada"), NotificationCenterLabels.English.GroupLabel));
        string? title = named.Title;
        Assert.Null(title);
        Assert.Equal(typeof(string), typeof(NotificationCenterLabels).GetProperty(nameof(NotificationCenterLabels.Title))!.PropertyType);
    }
}
