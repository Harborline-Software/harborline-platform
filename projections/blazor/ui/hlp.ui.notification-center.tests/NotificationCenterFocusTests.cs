using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// An outside-pointer observer whose callback a test can fire.
internal sealed class CapturingOutside : IOutsidePointerObserver
{
    public Func<OutsidePointerEvent, ValueTask>? Callback { get; private set; }
    public ValueTask<IOutsidePointerRegistration> ObserveAsync(ElementReference element, Func<OutsidePointerEvent, ValueTask> callback, OutsidePointerOptions? options = null, CancellationToken token = default) => ObserveAsync([element], callback, options, token);
    public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<ElementReference> elements, Func<OutsidePointerEvent, ValueTask> callback, OutsidePointerOptions? options = null, CancellationToken token = default) { Callback = callback; return new(new Registration(this)); }
    private sealed class Registration(CapturingOutside owner) : IOutsidePointerRegistration
    {
        public ValueTask DisposeAsync() { owner.Callback = null; return ValueTask.CompletedTask; }
        public ValueTask SetEnabledAsync(bool enabled) => ValueTask.CompletedTask;
        public void SetCallback(Func<OutsidePointerEvent, ValueTask> callback) => owner.Callback = callback;
    }
}

// Contract: focus is never stranded. An action that removes its own control asks the module to settle
// focus inside the dialog after the host's render, until focus has moved; the module moves it only if
// it fell out of the dialog (the Chromium probe checks that half). Labels never render empty.
public sealed class NotificationCenterFocusTests : BunitContext
{
    private readonly BunitJSModuleInterop module;
    public NotificationCenterFocusTests()
    {
        Services.AddSingleton<IOutsidePointerObserver>(new CapturingOutside());
        module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/notification-center.js");
        module.Mode = JSRuntimeMode.Loose;
    }

    private static readonly NotificationCenterItem[] Initial = [new("r1", "Export ready", "now", "result"), new("r2", "Import done", "later", "result"), new("r3", "Backup done", "earlier", "result", Read: true)];

    private sealed class Host : ComponentBase
    {
        [Parameter] public bool Late { get; set; }
        private IReadOnlyList<NotificationCenterItem> items = Initial;
        private Action? queued;
        public void Apply() { queued?.Invoke(); queued = null; StateHasChanged(); }
        private void Change(Action change) { if (Late) queued = change; else change(); }
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<HarborlineNotificationCenter>(0);
            builder.AddComponentParameter(1, nameof(HarborlineNotificationCenter.Items), items);
            builder.AddComponentParameter(2, nameof(HarborlineNotificationCenter.OnDismiss), EventCallback.Factory.Create<NotificationCenterItem>(this, item => Change(() => items = items.Where(entry => entry.Id != item.Id).ToArray())));
            builder.AddComponentParameter(3, nameof(HarborlineNotificationCenter.OnMarkAllRead), EventCallback.Factory.Create(this, () => Change(() => items = items.Select(entry => entry with { Read = true }).ToArray())));
            builder.CloseComponent();
        }
    }

    private IReadOnlyList<object?> LastSettle() => module.Invocations["settleFocus"].Last().Arguments;

    [Fact]
    public void DismissingSettlesFocusOnTheRowAtTheSamePositionAfterTheHostRemovesIt()
    {
        var cut = Render<Host>();
        cut.Find(".hl-notification-center__trigger").Click();
        cut.Find("[aria-label='Dismiss: Export ready']").Click();
        Assert.Empty(cut.FindAll("[aria-label='Dismiss: Export ready']"));
        Assert.Equal(0, LastSettle()[1]);
    }

    [Fact]
    public void MarkAllReadSettlesFocusInsideTheDialog()
    {
        var cut = Render<Host>();
        cut.Find(".hl-notification-center__trigger").Click();
        cut.Find(".hl-notification-center__header button").Click();
        Assert.Empty(cut.FindAll(".hl-notification-center__header button"));
        Assert.Equal(-1, LastSettle()[1]);
    }

    [Fact]
    public void ALateHostStillGetsASettleAfterItAppliesTheRemoval()
    {
        var cut = Render<Host>(p => p.Add(x => x.Late, true));
        cut.Find(".hl-notification-center__trigger").Click();
        cut.Find("[aria-label='Dismiss: Import done']").Click();
        var before = module.Invocations["settleFocus"].Count;
        cut.InvokeAsync(cut.Instance.Apply);
        Assert.Empty(cut.FindAll("[aria-label='Dismiss: Import done']"));
        Assert.True(module.Invocations["settleFocus"].Count > before);
        Assert.Equal(1, LastSettle()[1]);
    }

    [Fact]
    public void ASettledFocusIsNotRequestedAgainAndClosingCancelsAPendingOne()
    {
        module.Setup<bool>("settleFocus", _ => true).SetResult(true);
        var cut = Render<Host>();
        cut.Find(".hl-notification-center__trigger").Click();
        cut.Find("[aria-label='Dismiss: Export ready']").Click();
        var settled = module.Invocations["settleFocus"].Count;
        cut.Find(".hl-notification-center__tabs, .hl-notification-center__trigger").Click();
        Assert.Equal(settled, module.Invocations["settleFocus"].Count);
    }

    [Fact]
    public void BlankCallerLabelsKeepTheDefaultsAndNoNameIsEmpty()
    {
        var labels = new NotificationCenterLabels("Avisos", "", "", " ", "  ", "", "", _ => " ", "");
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, [new NotificationCenterItem("p1", "Approve", "now", "proposal"), new NotificationCenterItem("r1", "Done", "later", "result")])
            .Add(x => x.Labels, labels).Add(x => x.OnDismiss, _ => { }).Add(x => x.OnMarkAllRead, () => { }).Add(x => x.OnViewAll, () => { }));
        Assert.Equal("Avisos, 2 unread", cut.Find(".hl-notification-center__trigger").GetAttribute("aria-label"));
        cut.Find(".hl-notification-center__trigger").Click();
        Assert.NotNull(cut.Find("[aria-label='Confirm: Approve']"));
        Assert.NotNull(cut.Find("[aria-label='Dismiss: Done']"));
        foreach (var button in cut.FindAll("button")) Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("aria-label") ?? button.TextContent), button.OuterHtml);
        Assert.Equal("Mark all read", cut.Find(".hl-notification-center__header button").TextContent);
    }

    [Fact]
    public void CarriesArabicAndPseudoLocalizedCallerTextInAnRtlContainer()
    {
        var arabic = NotificationCenterLabels.English with { Title = "الإشعارات", Confirm = "تأكيد" };
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, [new NotificationCenterItem("p1", "Approve", "now", "proposal")]).Add(x => x.Labels, arabic).Add(x => x.Direction, "rtl"));
        cut.Find(".hl-notification-center__trigger").Click();
        Assert.Equal("rtl", cut.Find(".hl-notification-center").GetAttribute("dir"));
        Assert.Equal("الإشعارات", cut.Find("[role=dialog]").GetAttribute("aria-label"));
        Assert.NotNull(cut.Find("[aria-label='تأكيد: Approve']"));
        const string pseudo = "⟦ Ñöţîƒîçåţîöñš ··· ⟧";
        var pseudoCut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, Array.Empty<NotificationCenterItem>()).Add(x => x.Labels, NotificationCenterLabels.English with { Title = pseudo }));
        pseudoCut.Find(".hl-notification-center__trigger").Click();
        Assert.Equal(pseudo, pseudoCut.Find("h2").TextContent);
    }
}
