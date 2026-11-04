using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract (interface revision 4, T-1137): without caller Groups, one tab per built-in kind present (proposal, result,
// system), in order of first appearance, labelled from navigation.notifications.group.<kind>, then one last tab,
// navigation.notifications.group.other, for every custom kind. A kind id is never a label; caller Groups win.
// The shared fixture cases assert the same in both lanes; these facts also run where the fixture theory does not
// (Stryker's in-process run).
public sealed class NotificationCenterGroupTabsTests : BunitContext
{
    public NotificationCenterGroupTabsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IOutsidePointerObserver>(new CapturingOutside());
    }

    private static NotificationCenterItem Item(string id, string kind) => new(id, $"Title {id}", "now", kind);
    private static readonly NotificationCenterItem[] Mixed =
        [Item("d1", "deploy"), Item("p1", "proposal"), Item("a1", "audit"), Item("s1", "system"), Item("d2", "deploy"), Item("r1", "result")];

    private static string[] Tabs(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.FindAll("[role=tab]").Select(tab => tab.TextContent).ToArray();
    private static string[] Selected(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.FindAll("[role=tab]").Where(tab => tab.GetAttribute("aria-selected") == "true").Select(tab => tab.TextContent).ToArray();
    private static string[] Visible(IRenderedComponent<HarborlineNotificationCenter> cut) => cut.FindAll(".hl-notification-center__item strong").Select(node => node.TextContent.Replace("Title ", "", StringComparison.Ordinal)).ToArray();
    private static void Choose(IRenderedComponent<HarborlineNotificationCenter> cut, string label) => cut.FindAll("[role=tab]").Single(tab => tab.TextContent == label).Click();

    private IRenderedComponent<HarborlineNotificationCenter> Open(IReadOnlyList<NotificationCenterItem> items, IReadOnlyList<NotificationGroup>? groups = null)
    {
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, items).Add(x => x.Groups, groups));
        cut.Find(".hl-notification-center__trigger").Click();
        return cut;
    }

    [Fact]
    public void DerivedTabsAreCatalogLabelsWithOneLastTabForEveryCustomKind()
    {
        var cut = Open(Mixed);
        Assert.Equal(["All", "Proposals", "System", "Results", "Other"], Tabs(cut));
        Assert.Equal(["All"], Selected(cut));
        Choose(cut, "Other");
        // Every tab states its selection: "true" on the chosen tab and "false", never an empty value, on the rest.
        Assert.Equal(["false", "false", "false", "false", "true"], cut.FindAll("[role=tab]").Select(tab => tab.GetAttribute("aria-selected") ?? "(absent)").ToArray());
        Assert.Equal(["d1", "a1", "d2"], Visible(cut));
        Choose(cut, "System");
        Assert.Equal(["System"], Selected(cut));
        Assert.Equal(["s1"], Visible(cut));
        Choose(cut, "Proposals");
        Assert.Equal(["p1"], Visible(cut));
        Choose(cut, "All");
        Assert.Equal(["All"], Selected(cut));
        Assert.Equal(["d1", "p1", "a1", "s1", "d2", "r1"], Visible(cut));
    }

    [Fact]
    public void CallerGroupsKeepTheirLabelsAndFilterByKind()
    {
        var cut = Open(Mixed, [new("proposal", "Approvals"), new("deploy", "Deployments")]);
        Assert.Equal(["All", "Approvals", "Deployments"], Tabs(cut));
        Choose(cut, "Deployments");
        Assert.Equal(["d1", "d2"], Visible(cut));
    }

    [Fact]
    public void ASelectedTabThatDisappearsFallsBackToAll()
    {
        // Select a caller group, then drop Groups: the derived tabs have no such key and, with only custom kinds,
        // no tab list, so the view must return to All rather than keep filtering by the vanished tab.
        NotificationCenterItem[] custom = [Item("d1", "deploy"), Item("a1", "audit")];
        var cut = Open(custom, [new("deploy", "Deployments"), new("audit", "Audit log")]);
        Choose(cut, "Deployments");
        Assert.Equal(["d1"], Visible(cut));
        cut.Render(p => p.Add(x => x.Items, custom).Add(x => x.Groups, (IReadOnlyList<NotificationGroup>?)null));
        Assert.Empty(cut.FindAll("[role=tablist]"));
        Assert.Equal(["d1", "a1"], Visible(cut));
        // The fallback is kept: the caller group coming back does not reselect it.
        cut.Render(p => p.Add(x => x.Items, custom).Add(x => x.Groups, new NotificationGroup[] { new("deploy", "Deployments"), new("audit", "Audit log") }));
        Assert.Equal(["All"], Selected(cut));
    }

    [Fact]
    public void OnlyCustomKindsMakeOneTabAndSoNoTabList()
    {
        Assert.Empty(Open([Item("d1", "deploy"), Item("a1", "audit")]).FindAll("[role=tablist]"));
        Assert.Empty(Open([Item("p1", "proposal"), Item("p2", "proposal")]).FindAll("[role=tablist]"));
    }

    [Fact]
    public void ThePseudoLocaleLabelsEveryTab()
    {
        var pseudo = new Dictionary<string, string>
        {
            ["navigation.notifications.all"] = "⟦ Åļļ ··· ⟧", ["navigation.notifications.group.proposal"] = "⟦ Þŕöþöšåļš ····· ⟧",
            ["navigation.notifications.group.result"] = "⟦ Ŕëšûļţš ···· ⟧", ["navigation.notifications.group.system"] = "⟦ Šýšţëɱ ··· ⟧",
            ["navigation.notifications.group.other"] = "⟦ Öţĥëŕ ··· ⟧",
        };
        var cut = Render<HarborlineLocaleProvider>(p => p.Add(x => x.Locale, "en-XA").Add(x => x.Catalog, pseudo)
            .AddChildContent<HarborlineNotificationCenter>(c => c.Add(x => x.Items, Mixed)));
        cut.Find(".hl-notification-center__trigger").Click();
        Assert.Equal(["⟦ Åļļ ··· ⟧", "⟦ Þŕöþöšåļš ····· ⟧", "⟦ Šýšţëɱ ··· ⟧", "⟦ Ŕëšûļţš ···· ⟧", "⟦ Öţĥëŕ ··· ⟧"], cut.FindAll("[role=tab]").Select(tab => tab.TextContent).ToArray());
    }
}
