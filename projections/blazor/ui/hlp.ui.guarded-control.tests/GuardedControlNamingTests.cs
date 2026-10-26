using AngleSharp.Dom;
using Bunit;
using Harborline.Foundation.Localization;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
using Clock = Harborline.UIAdapters.Blazor.Tests.GuardedControlAnnouncementTests;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Interface revision 2 (T-1105 item 1, owner ruling 2026-10-26, shape A). The visible action label is
/// the accessible name in every state; the guard instruction is a localized whole message that is the
/// action's accessible description, after the caller's DescribedBy ids, and is rendered visibly. The
/// description holds still while the countdown ticks, and the live region announces the armed message
/// once per transition. The React lane asserts the same values (GuardedControl.naming.test.tsx).
/// </summary>
public sealed class GuardedControlNamingTests : BunitContext
{
    private const string Label = "Publish";

    public GuardedControlNamingTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static string Isolated(string text) => $"⁨{text}⁩";
    private static string Covered(string label) => $"Guarded action: {Isolated(label)}. Activate to arm.";
    private static string Armed(string label) => $"Armed for {Isolated(label)}. Activate again within 5 seconds to commit.";
    private static string Committing(string label) => $"Committing {Isolated(label)}.";

    [Fact]
    public void NamesTheActionByItsVisibleLabelInEveryState()
    {
        var operation = new TaskCompletionSource();
        var cut = RenderGuard(commit: () => operation.Task);
        Assert.Equal(Label, GuardAccessibility.Name(cut.Find("button"), cut));
        cut.Find("button").Click();
        Assert.Equal("armed", cut.Find("button").GetAttribute("data-guard-state"));
        Assert.Equal("5 seconds", cut.Find("[role=timer]").TextContent);
        Assert.Equal(Label, GuardAccessibility.Name(cut.Find("button"), cut));
        _ = cut.Find("button").ClickAsync(new MouseEventArgs());
        Assert.Equal("committing", cut.Find("button").GetAttribute("data-guard-state"));
        Assert.Equal(Label, GuardAccessibility.Name(cut.Find("button"), cut));
    }

    [Fact]
    public void DescribesEachStateAfterTheCallerDescribedByIds()
    {
        var operation = new TaskCompletionSource();
        var host = Render(WithCallerNote("note", "Signing records your name.", Label, commit: () => operation.Task));
        var button = () => host.Find("button");
        Assert.Equal($"Signing records your name. {Covered(Label)}", GuardAccessibility.Description(button(), host));
        Assert.Equal($"note {host.Find(".hl-guarded-control__instruction").Id}", button().GetAttribute("aria-describedby"));
        button().Click();
        Assert.Equal($"Signing records your name. {Armed(Label)}", GuardAccessibility.Description(button(), host));
        _ = button().ClickAsync(new MouseEventArgs());
        Assert.Equal($"Signing records your name. {Committing(Label)}", GuardAccessibility.Description(button(), host));
    }

    [Fact]
    public void DescribesTheDisabledCoveredActionWithTheDenialAndTheInstruction()
    {
        var host = Render(WithCallerNote("denial", "Complete the affirmation first.", "Delete", disabled: true));
        var button = host.Find("button");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("Delete", GuardAccessibility.Name(button, host));
        Assert.Equal($"Complete the affirmation first. {Covered("Delete")}", GuardAccessibility.Description(button, host));
    }

    [Fact]
    public void RendersTheInstructionVisiblyAsTheElementTheDescriptionReferences()
    {
        var cut = RenderGuard();
        var element = Assert.Single(cut.FindAll(".hl-guarded-control__instruction"));
        Assert.Equal(Covered(Label), element.TextContent);
        Assert.Null(element.Closest("[aria-hidden=true], [hidden], .hl-visually-hidden"));
        Assert.Equal(element.Id, cut.Find("button").GetAttribute("aria-describedby"));
        cut.Find("button").Click();
        Assert.Equal(Armed(Label), cut.Find(".hl-guarded-control__instruction").TextContent);
    }

    [Fact]
    public async Task KeepsTheArmedNameAndDescriptionStillWhileTheCountdownTicks()
    {
        var clock = new Clock.ManualClock();
        var cut = RenderGuard();
        Clock.UseClock(cut.Instance, clock);
        cut.Find("button").Click();
        foreach (var timer in new[] { "5 seconds", "4 seconds", "3 seconds", "2 seconds", "1 second" })
        {
            cut.WaitForAssertion(() => Assert.Equal(timer, cut.Find("[role=timer]").TextContent));
            Assert.Equal(Label, GuardAccessibility.Name(cut.Find("button"), cut));
            Assert.Equal(Armed(Label), GuardAccessibility.Description(cut.Find("button"), cut));
            Assert.Equal(Armed(Label), cut.Find(".hl-guarded-control__instruction").TextContent);
            await Clock.TickAsync(cut, clock);
        }
        cut.WaitForAssertion(() => Assert.Equal("covered", cut.Find("button").GetAttribute("data-guard-state")));
        Assert.Equal(Covered(Label), GuardAccessibility.Description(cut.Find("button"), cut));
    }

    // The live region's text is read after the arming render and after every countdown render: it is
    // the armed message throughout, so no tick changes what the region would announce. Recovery
    // changes it once, and the next arm sets the armed message again, once.
    [Fact]
    public async Task AnnouncesTheArmedMessageOncePerTransitionAndNeverTheCountdown()
    {
        var clock = new Clock.ManualClock();
        var cut = RenderGuard();
        Clock.UseClock(cut.Instance, clock);
        var statuses = new List<string>();
        cut.Find("button").Click();
        statuses.Add(cut.Find("[role=status]").TextContent);
        for (var second = 0; second < 4; second++)
        {
            await Clock.TickAsync(cut, clock);
            statuses.Add(cut.Find("[role=status]").TextContent);
        }
        Assert.Equal("1 second", cut.Find("[role=timer]").TextContent);
        Assert.All(statuses, status => Assert.Equal(Armed(Label), status));
        await Clock.TickAsync(cut, clock);
        cut.WaitForAssertion(() => Assert.Equal($"Guard restored for {Isolated(Label)}.", cut.Find("[role=status]").TextContent));
        cut.Find("button").Click();
        Assert.Equal(Armed(Label), cut.Find("[role=status]").TextContent);
        Assert.DoesNotContain(cut.FindAll("[aria-live]"), region => region.QuerySelector("[role=timer]") is not null);
    }

    [Fact]
    public void HostCatalogLocalizesTheDescriptionsWhileTheNameStaysTheLabel()
    {
        var operation = new TaskCompletionSource();
        var cut = RenderInLocale("fr-FR", "Publier", new Dictionary<string, string>
        {
            ["chrome.guard.covered.instruction"] = "Action protégée : {action}. Activez pour armer.",
            ["chrome.guard.armed.whole"] = "Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.",
            ["chrome.guard.committing.status"] = "Validation de {action}.",
            ["chrome.guard.duration.other"] = "{seconds} secondes",
        }, commit: () => operation.Task);
        Assert.Equal("Publier", GuardAccessibility.Name(cut.Find("button"), cut));
        Assert.Equal($"Action protégée : {Isolated("Publier")}. Activez pour armer.", GuardAccessibility.Description(cut.Find("button"), cut));
        cut.Find("button").Click();
        Assert.Equal("Publier", GuardAccessibility.Name(cut.Find("button"), cut));
        // The duration is the catalog's chrome.guard.duration form in this lane (T-1105's deferred duration parity).
        Assert.Equal($"Armé pour {Isolated("Publier")}. Activez de nouveau dans un délai de 5 secondes pour valider.", GuardAccessibility.Description(cut.Find("button"), cut));
        _ = cut.Find("button").ClickAsync(new MouseEventArgs());
        Assert.Equal($"Validation de {Isolated("Publier")}.", GuardAccessibility.Description(cut.Find("button"), cut));
    }

    [Fact]
    public void ArabicDescriptionsIsolateTheLabelRightToLeft()
    {
        var cut = RenderInLocale("ar-SA", "تصدير", new Dictionary<string, string>
        {
            ["chrome.guard.covered.instruction"] = "إجراء محمي: {action}. فعّل للتجهيز.",
            ["chrome.guard.armed.whole"] = "تم تجهيز {action}. فعّل مرة أخرى خلال {duration} للتأكيد.",
            ["chrome.guard.duration.few"] = "{seconds} ثوانٍ",
            ["chrome.guard.duration.other"] = "{seconds} ثانية",
        }, Clock.HostFormatter("arab"));
        Assert.Equal("rtl", cut.Find(".hl-guarded-control").GetAttribute("dir"));
        Assert.Equal("تصدير", GuardAccessibility.Name(cut.Find("button"), cut));
        Assert.Equal($"إجراء محمي: {Isolated("تصدير")}. فعّل للتجهيز.", GuardAccessibility.Description(cut.Find("button"), cut));
        cut.Find("button").Click();
        Assert.Equal("تصدير", GuardAccessibility.Name(cut.Find("button"), cut));
        Assert.Equal($"تم تجهيز {Isolated("تصدير")}. فعّل مرة أخرى خلال ٥ ثوانٍ للتأكيد.", GuardAccessibility.Description(cut.Find("button"), cut));
    }

    [Fact]
    public void PseudoLocaleLocalizesTheDescriptions()
    {
        const string label = "⟦ Šîĝñ åñđ šåṽë ······ ⟧";
        var cut = RenderInLocale("en-XA", label, new Dictionary<string, string>
        {
            ["chrome.guard.covered.instruction"] = "⟦ Ĝüåŕðéð åçţîöñ: {action}. Åçţîṽåţé ţö åŕɱ. ········ ⟧",
        });
        Assert.Equal(label, GuardAccessibility.Name(cut.Find("button"), cut));
        Assert.Equal($"⟦ Ĝüåŕðéð åçţîöñ: {Isolated(label)}. Åçţîṽåţé ţö åŕɱ. ········ ⟧", GuardAccessibility.Description(cut.Find("button"), cut));
        Assert.Equal($"⟦ Ĝüåŕðéð åçţîöñ: {Isolated(label)}. Åçţîṽåţé ţö åŕɱ. ········ ⟧", cut.Find(".hl-guarded-control__instruction").TextContent);
    }

    private IRenderedComponent<HarborlineGuardedControl> RenderGuard(Func<Task>? commit = null) =>
        Render<HarborlineGuardedControl>(p => p
            .Add(x => x.Action, "packages.export")
            .Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, Label)
            .Add(x => x.OnCommit, () => commit?.Invoke() ?? Task.CompletedTask));

    private IRenderedComponent<HarborlineGuardedControl> RenderInLocale(
        string locale, string label, IReadOnlyDictionary<string, string> catalog, HarborlineNumberFormatter? numberFormatter = null, Func<Task>? commit = null) =>
        Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, locale)
            .Add(x => x.Catalog, catalog)
            .Add(x => x.NumberFormatter, numberFormatter)
            .AddChildContent<HarborlineGuardedControl>(guard => guard
                .Add(x => x.Action, "packages.export")
                .Add(x => x.ClassificationId, "new-record-type")
                .Add(x => x.Label, label)
                .Add(x => x.OnCommit, () => commit?.Invoke() ?? Task.CompletedTask)))
        .FindComponent<HarborlineGuardedControl>();

    /// <summary>The caller's own description element beside the guard, which DescribedBy names.</summary>
    internal static RenderFragment WithCallerNote(string id, string text, string label, Func<Task>? commit = null, bool disabled = false) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", id);
        builder.AddContent(2, text);
        builder.CloseElement();
        builder.OpenComponent<HarborlineGuardedControl>(3);
        builder.AddAttribute(4, nameof(HarborlineGuardedControl.Action), "packages.export");
        builder.AddAttribute(5, nameof(HarborlineGuardedControl.ClassificationId), "new-record-type");
        builder.AddAttribute(6, nameof(HarborlineGuardedControl.Label), label);
        builder.AddAttribute(7, nameof(HarborlineGuardedControl.DescribedBy), id);
        builder.AddAttribute(8, nameof(HarborlineGuardedControl.Disabled), disabled);
        builder.AddAttribute(9, nameof(HarborlineGuardedControl.OnCommit), EventCallback.Factory.Create(new object(), () => commit?.Invoke() ?? Task.CompletedTask));
        builder.CloseComponent();
    };
}

/// <summary>
/// The parts of the accessible name and description computation (accname 1.2, §4.3) that a guarded
/// action uses, over bUnit's rendered markup, which has no accessibility tree of its own: a name from
/// aria-labelledby, else aria-label, else the content that is not aria-hidden; a description from the
/// elements aria-describedby names, in order. The React lane computes both with dom-accessibility-api.
/// </summary>
internal static class GuardAccessibility
{
    public static string Name<T>(IElement element, IRenderedComponent<T> scope) where T : IComponent
    {
        var labelledBy = element.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy)) return Referenced(labelledBy, scope);
        var label = element.GetAttribute("aria-label");
        return Normalize(string.IsNullOrWhiteSpace(label) ? Content(element) : label);
    }

    public static string Description<T>(IElement element, IRenderedComponent<T> scope) where T : IComponent =>
        Referenced(element.GetAttribute("aria-describedby") ?? string.Empty, scope);

    private static string Referenced<T>(string ids, IRenderedComponent<T> scope) where T : IComponent =>
        Normalize(string.Join(" ", ids.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => scope.FindAll($"[id='{id}']") is { Count: > 0 } found ? found[0].TextContent : string.Empty)));

    private static string Content(INode node) => node switch
    {
        IElement element when element.GetAttribute("aria-hidden") == "true" => string.Empty,
        IElement element when node.Parent is not null && !string.IsNullOrWhiteSpace(element.GetAttribute("aria-label")) => element.GetAttribute("aria-label")!,
        IElement element => string.Concat(element.ChildNodes.Select(Content)),
        _ => node.TextContent,
    };

    // dom-accessibility-api's final step: trim, and collapse runs of two or more whitespace characters.
    private static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s\s+", " ");
}
